using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using coder.net.core;
using coder.net.core.common;
using coder.net.core.pubsub.messages;
using coder.net.core.threading;
using Microsoft.Extensions.Logging;
using PubSub;

namespace coder.net.transport
{
    public class TcpServer : RunningTask, ITcpServer
    {
        public TcpClient Client { get; protected set; }

        protected TcpListener Listener { get; set; }

        protected readonly IServerConfiguration _config;

        protected IPAddress IpAddress { get; set; }
        protected int Port { get; }
        protected int Connections { get; }
        protected bool ListenerDelay { get; }
        protected int DelayDuration { get; }
        protected int ReadTimeout { get; }
        protected bool KeepConnectionOpen { get; }
        protected bool ShutdownClientOnOpenSocket { get; }
        protected bool RaiseEventOnReceive { get; private set; }

        public TcpServer(ILoggerFactory loggerFactory, IServerConfiguration config)
            : base(loggerFactory)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));

            if (!IPAddress.TryParse(_config.IpAddress, out var ipAddress))
            {
                IpAddress = IPAddress.Any;
            }
            else
            {
                IpAddress = ipAddress;
            }

            Name = _config.Name;
            Port = _config.Port;
            Connections = _config.ConnectionCount;
            ListenerDelay = _config.ListenerDelay;
            DelayDuration = _config.DelayDuration;
            KeepConnectionOpen = _config.KeepConnectionOpen;
            ShutdownClientOnOpenSocket = _config.ShutdownClientOnOpenSocket;
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if(Disposed)
                {
                    return;
                }

                if (disposing)
                {
                    if (Client != null && Client.Client != null && Client.Client.Connected)
                    {
                        Client?.Client?.Shutdown(SocketShutdown.Both);
                        Client?.Close();
                        Listener?.Stop();

                        Client?.Dispose();

                        Client = null;
                    }

                    Listener = null;
                }
            }
            catch (SocketException sox)
            {
                Logger.LogError(sox, $"Socket Exception while disposing of the Server.  The Error Code is {sox.ErrorCode}.");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Disposing of the Server has caused an exception.");
            }

            base.Dispose(disposing);
        }

        public override async Task<bool> Run()
        {
            try
            {
                await SpawnServer().ConfigureAwait(false);
            }
            catch (OperationCanceledException oce)
            {
                Stopped = true;
                Logger.LogInformation($"Server on {IpAddress}:{Port} has shut down.  Attempting to restart...");
                StopToken?.Dispose();

                StopToken = new CancellationTokenSource();
                EventHub.Publish(new StartMessage(UniqueIdentifier, true));
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Thread running Server on {IpAddress}:{Port} has crashed.  Attempting to restart it...");
                OnError(ex);
                return false;
            }

            return true;
        }

        public override bool Stop()
        {
            try
            {
                Logger.LogWarning($"Stopping server on {IpAddress}:{Port}.");
                Listener.Stop();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error while stopping TcpServer on {IpAddress}:{Port}.");
                return false;
            }

            return base.Stop();
        }

        public override async Task Restart()
        {
            try
            {
                if (!Restarting)
                {
                    Restarting = true;

                    Logger.LogWarning($"Restarting server on {IpAddress}:{Port}.");

                    if (!Stopped)
                    {
                        Listener?.Stop();
                        await this.Timeout(2);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Exception while restarting server at {IpAddress}:{Port}");
                EventHub.Publish(new StartMessage(UniqueIdentifier, true));
            }

            await base.Restart();
        }

        protected async Task SpawnServer()
        {
            await Task.Factory.StartNew(
                            async () =>
                            {
                                if (Stopped)
                                {
                                    Stopped = false;

                                    Logger.LogInformation($"Starting server on {IpAddress}:{Port}.");

                                    await RunServerAsync();

                                    Stopped = true;
                                }
                            }, StopToken.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap().ConfigureAwait(false);
        }

        protected virtual async Task RunServerAsync()
        {
            try
            {
                if (KeepConnectionOpen)
                {
                    await OpenSocketAsync();
                    await ReadAndNotifyAsync();
                }
                else
                {
                    await OpenAndReadAsync();
                }
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning($"Server on {IpAddress}:{Port} is shutting down...");
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error while running the Server on {IpAddress}:{Port}...");
                OnError(ex);
            }
        }

        protected virtual async Task OpenAndReadAsync()
        {
            while (!Restarting && !Stopped)
            {
                await OpenSocketAsync();
                var data = await ReadAsync();
                OnData(data);
            }
        }

        protected virtual async Task ReadAndNotifyAsync()
        {
            while (!Restarting && !Stopped)
            {
                var data = await ReadAsync();
                OnData(data);

                StopToken.Token.ThrowIfCancellationRequested();
            }
        }

        protected virtual async Task<Memory<byte>> ReadAsync()
        {
            Logger.LogDebug($"Accepted connection from {Client?.Client?.RemoteEndPoint}");

            if (RaiseEventOnReceive)
            {
                await EventHub.PublishAsync(new Message(UniqueIdentifier));
                RaiseEventOnReceive = false;
            }

            var data = await ReceiveAsync();
            return data;
        }

        protected virtual async Task OpenSocketAsync()
        {
            await StartListeningAsync();

            Logger.LogDebug($"Waiting for connection on port {Port}");

            // use Task.Run to pass the cancellation token so the Listener stops when the Token is cancelled.
            Client = await Task.Run(() => Listener?.AcceptTcpClientAsync(), StopToken.Token);
        }

        protected virtual void OpenSocket()
        {
            if (ShutdownClientOnOpenSocket)
            {
                if (Client != null)
                {
                    Client?.Client?.Shutdown(SocketShutdown.Both);

                    Listener?.Server?.Close();
                }
            }

            if (Listener != null)
            {
                Listener.Stop();
            }
            else
            {
                Listener = new TcpListener(GetServerAddress());
            }

            Listener.Start(Connections);
        }

        protected virtual async Task StartListeningAsync()
        {
            try
            {
                if (ListenerDelay)
                {
                    await Task.Delay(DelayDuration, StopToken.Token);
                }

                OpenSocket();

                Logger.LogInformation($"Server is listening for connections on {IpAddress}:{Port}...");
            }
            catch (SocketException sox)
            {
                Logger.LogError(sox, $"Socket cannot be opened on {IpAddress}:{Port}.");
                if (sox.ErrorCode.Equals(10049))
                {
                    Logger.LogInformation($"There is no client connected to {IpAddress}:{Port}.  Please connect one to continue.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Unable to start Server configured to listen on {IpAddress}:{Port}.");
                OnError(ex);
            }
        }

        protected async Task<Memory<byte>> ReceiveAsync()
        {
            Logger.LogDebug($"Received connection request from {Client?.Client?.RemoteEndPoint?.ToString()}.");

            var data = new byte[1024];

            try
            {
                if (Client != null)
                {
                    int bytes = 0;
                    var safeStream = Stream.Synchronized(Client.GetStream());
                    var readTask = safeStream.ReadAsync(data, 0, 1024, StopToken.Token);

                    if (ReadTimeout > 0)
                    {
                        var delayTask = Task.Delay(ReadTimeout, StopToken.Token);
                        var t = await Task.WhenAny(readTask, delayTask);

                        if (t.Equals(readTask))
                        {
                            bytes = await readTask;
                        }
                        else
                        {
                            // if ReadTimeout is defined and exceeded, restart the server by cancelling the token
                            StopToken.Cancel();
                        }
                    }
                    else
                    {
                        bytes = await readTask;
                    }
                }
            }
            catch (ObjectDisposedException ode)
            {
                Logger.LogError(ode, $"The socket was disposed before the Read operation completed on {IpAddress}:{Port}.");
            }
            catch (OperationCanceledException)
            {
                StopToken.Dispose();
                StopToken = new CancellationTokenSource();
                EventHub.Publish(new StartMessage(UniqueIdentifier, true));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error while processing request from {Client?.Client?.RemoteEndPoint?.ToString()} on {IpAddress}:{Port}.");
                OnError(ex);
            }

            return new Memory<byte>(data);
        }

        protected virtual void OnData(Memory<byte> data)
        {
            Logger.LogInformation($"Received message from client at server listening on {IpAddress}:{Port} that is [{data.Length}] bytes long.");
            EventHub.Publish(new DataMessage(UniqueIdentifier, data));
        }

        protected void OnError(Exception ex)
        {
            EventHub.Publish(new ErrorMessage(UniqueIdentifier, ex));
        }

        protected virtual IPEndPoint GetServerAddress()
        {
            return new IPEndPoint(IpAddress, Port);
        }
    }
}
