using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using coder.net.configuration;
using coder.net.core;
using coder.net.core.pubsub.messages;
using coder.net.core.threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace coder.net.transport
{
    public class Client : RunningTask, IClient
	{
		protected TcpClient Sender { get; set; }

		protected readonly IClientConfiguration Config;
		protected ConcurrentQueue<Memory<byte>> Queue { get; } = new ConcurrentQueue<Memory<byte>>();
		protected IPAddress IpAddress { get; }
		protected int Port { get; }

		public Client(ILoggerFactory loggerFactory, ClientConfiguration config)
			: base(loggerFactory)
		{
			Config = config ?? throw new ArgumentNullException(nameof(config));

			Name = Config.Name;

			if (!IPAddress.TryParse(Config.RemoteIpAddress, out var ipAddress))
			{
				IpAddress = IPAddress.Any;
			}
			else
			{
				IpAddress = ipAddress;
			}

			Port = Config.RemotePort;
		}

		protected override void Dispose(bool disposing)
		{
			try
			{
				if (Disposed)
				{
					return;
				}

				if (disposing)
				{
					if (Sender != null)
					{
						if (Sender.Client != null && Sender.Client.Connected)
						{
							Sender?.Client?.Shutdown(SocketShutdown.Both);
							Sender?.Client?.Close();
							Sender?.Close();
						}

						Sender?.Dispose();
						Sender = null;
					}
				}
			}
			catch (SocketException sox)
			{
				Logger.LogError(sox, $"Socket Exception while disposing of the Client {Name} at {IpAddress}:{Port}.  The Error Code is {sox.ErrorCode}.");
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, $"Disposing of the Client {Name} at {IpAddress}:{Port} has caused an exception.");
			}

			base.Dispose(disposing);
		}

		public bool Send(Memory<byte> data)
		{
			try
			{
				Logger.LogDebug($"Queuing byte stream data for {Name} - {IpAddress}:{Port}.");

				Queue.Enqueue(data);
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, $"Error while queuing data for {Name} - {IpAddress}:{Port}.");
				return false;
			}

			return true;
		}

		public override async Task<bool> Run()
		{
			try
			{
				await SpawnProcess().ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				Stopped = true;
				Logger.LogInformation($"Client responding on {Name} - {IpAddress}:{Port} has shut down.");
				StopToken?.Dispose();

                StopToken = new CancellationTokenSource();
				EventHub.Publish(new StartMessage(UniqueIdentifier, true));
				return false;
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, $"Thread running Client responding on {Name} - {IpAddress}:{Port} has crashed.  Attempting to restart it...");
				OnError(ex);
			}

			return true;
		}

		public override bool Stop()
		{
			Logger.LogWarning($"Stopping client {Name} responding on {IpAddress}:{Port}.");
			Sender?.Close();
			return base.Stop();
		}

		public override async Task Restart()
		{
			try
			{
				if (!Restarting)
				{
					Restarting = true;

                    Logger.LogWarning($"Restarting client {Name} responding on {IpAddress}:{Port}.");

					if (!Stopped)
					{
						Sender?.Close();
						await Timeout(2);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, $"Client {Name} has thrown an exception while restarting from {IpAddress}:{Port}.");
				EventHub.Publish(new StartMessage(UniqueIdentifier, true));
			}

			await base.Restart();
		}

		protected override async Task RunAsync()
		{
			try
			{
				while (!Restarting && !Stopped)
				{
					if (!Queue.IsEmpty)
					{
						await ConnectAndSendAsync();
					}

					StopToken.Token.ThrowIfCancellationRequested();
				}
			}
			catch (OperationCanceledException)
			{
				Logger.LogWarning($"Client {Name} responding to server at {IpAddress}:{Port} is shutting down...");
				throw;
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, $"Error in client {Name} while sending to {IpAddress}:{Port}.");
				OnError(ex);
			}
		}

		protected virtual async Task ConnectAndSendAsync()
		{
			CreateSocket();

			var connection_count = 0;

			while (!await ConnectAsync())
			{
				if (++connection_count > 50)
				{
					var ex = new Exception($"Failed to connect to client after 50 tries.  Giving up and restarting.");
					Logger.LogError(ex, $"Client cannot connect to server after 50 attempts.  Restarting client...");
					OnError(ex);
				}
			}

			if (Queue.TryDequeue(out var packet))
			{
				Logger.LogInformation($"Sending packet to server from client {Name} on {IpAddress}:{Port}.");
				await SendAsync(packet);
			}
		}

		private void CreateSocket()
		{
			if (Sender?.Client == null || !Sender.Connected)
			{
				Logger.LogDebug($"{Name} client is creating a new client socket for {IpAddress}:{Port}.");
				Sender = new TcpClient(AddressFamily.InterNetwork);
			}
		}

		private async Task<bool> ConnectAsync()
		{
			try
			{
				if (!Sender.Connected)
				{
					Logger.LogDebug($"Client {Name} is attempting to connect on {IpAddress}:{Port}.");

					await Task.Run(() => Sender.ConnectAsync(IpAddress, Port), StopToken.Token);

					Logger.LogDebug($"Client {Name} has established a connection to {IpAddress}:{Port}.");
				}
			}
			catch (ObjectDisposedException ode)
			{
				Logger.LogWarning(ode, $"Client {Name} has been disposed.  Recreating the Socket on {IpAddress}:{Port}.");
				Sender.Dispose();
				Sender = null;

				CreateSocket();

				return false;
			}
			catch (SocketException se)
			{
				Logger.LogWarning(se, $"Client {Name} cannot connect to server on {IpAddress}:{Port}.");
				return false;
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, $"Client {Name} failed to connect to remote host at {IpAddress}:{Port}.");
				OnError(ex);
				return false;
			}

			return true;
		}

		private async Task SendAsync(Memory<byte> packet)
		{
			try
			{
				var safeStream = Stream.Synchronized(Sender.GetStream());
				var writer = new BinaryWriter(safeStream);
				writer.Write(packet.ToArray());

				if (!await WaitForAckAsync(safeStream))
				{
					Logger.LogWarning($"Server has not responded with an ack.  The last message sent by client {Name} may have been lost on {IpAddress}:{Port}.");
				}
			}
			catch (ObjectDisposedException ode)
			{
				Logger.LogError(ode, $"The client {Name} socket was disposed before the Send operation completed to {IpAddress}:{Port}");
			}
			catch (OperationCanceledException)
			{
				StopToken?.Dispose();
				StopToken = new CancellationTokenSource();
				EventHub.Publish(new StartMessage(UniqueIdentifier, true));
			}
			catch (SocketException sox)
			{
				Logger.LogError(sox, $"Socket Exception on Client {Name} sending on {IpAddress}:{Port}.");
			}
			catch (Exception ex)
			{
				Logger.LogError($"Client {Name} failed to Send to host from {IpAddress}:{Port}.");
				OnError(ex);
			}

		}

		protected virtual async Task<bool> WaitForAckAsync(Stream stream)
		{
			return await Task.FromResult(true);
		}
	}
}
