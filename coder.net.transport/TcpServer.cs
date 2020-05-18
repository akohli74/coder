using System;
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
    public class TcpServer : RunningTask, ITcpServer
    {
        public TcpClient Client { get; protected set; }

        protected TcpListener Listener { get; set; }

        protected readonly ServerConfiguration Config;

        protected IPAddress IpAddress { get; set; }
        protected int Port { get; }
        protected int Connections { get; }
        protected bool ListenerDelay { get; }
        protected int DelayDuration { get; }
        protected int ReadTimeout { get; }
        protected bool KeepConnectionOpen { get; }
        protected bool ShutdownClientOnOpenSocket { get; }
        protected bool RaiseEventOnReceive { get; private set; }

        public TcpServer(ILoggerFactory loggerFactory, ServerConfiguration config)
            : base(loggerFactory)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));

            if (!IPAddress.TryParse(Config.IpAddress, out var ipAddress))
            {
                IpAddress = IPAddress.Any;
            }
            else
            {
                IpAddress = ipAddress;
            }

            Name = Config.Name;
            Port = Config.Port;
            Connections = Config.ConnectionCount;
            ListenerDelay = Config.ListenerDelay;
            DelayDuration = Config.DelayDuration;
            KeepConnectionOpen = Config.KeepConnectionOpen;
            ShutdownClientOnOpenSocket = Config.ShutdownClientOnOpenSocket;
            RaiseEventOnReceive = Config.RaiseEventOnReceive;
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
                Logger.LogError(sox, $"Socket Exception while disposing of the Server {Name}.  The Error Code is {sox.ErrorCode}.");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Disposing of the Server {Name} has caused an exception.");
            }

            base.Dispose(disposing);
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
                Logger.LogInformation($"Server {Name} on {IpAddress}:{Port} has shut down.  Attempting to restart...");
                StopToken?.Dispose();

                StopToken = new CancellationTokenSource();
                EventHub.Publish(new StartMessage(UniqueIdentifier, true));
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Thread running server {Name} on {IpAddress}:{Port} has crashed.  Attempting to restart it...");
                OnError(ex);
                return false;
            }

            return true;
        }

        public override bool Stop()
        {
            try
            {
                Logger.LogWarning($"Stopping server {Name} on {IpAddress}:{Port}.");
                Listener.Stop();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error while stopping server {Name} on {IpAddress}:{Port}.");
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

                    Logger.LogWarning($"Restarting server {Name} on {IpAddress}:{Port}.");

                    if (!Stopped)
                    {
                        Listener?.Stop();
                        await Timeout(2);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Exception while restarting server {Name} at {IpAddress}:{Port}");
                EventHub.Publish(new StartMessage(UniqueIdentifier, true));
            }

            await base.Restart();
        }

        protected override async Task RunAsync()
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
                Logger.LogWarning($"Server {Name} on {IpAddress}:{Port} is shutting down...");
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error while running the server {Name} on {IpAddress}:{Port}...");
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
            Logger.LogDebug($"Server {Name} accepted connection from {Client?.Client?.RemoteEndPoint}");

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

            Logger.LogDebug($"Server {Name} is waiting for connection on port {Port} at {IpAddress}...");

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

                Logger.LogInformation($"Server {Name} is listening for connections on {IpAddress}:{Port}...");
            }
            catch (SocketException sox)
            {
                Logger.LogError(sox, $"Server {Name} socket cannot be opened on {IpAddress}:{Port}.");
                if (sox.ErrorCode.Equals(10049))
                {
                    Logger.LogInformation($"There is no client connected to {IpAddress}:{Port} on server {Name}.  Please connect one to continue.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Unable to start server {Name} configured to listen on {IpAddress}:{Port}.");
                OnError(ex);
            }
        }

        protected async Task<Memory<byte>> ReceiveAsync()
        {
            Logger.LogDebug($"Server {Name} received connection request from {Client?.Client?.RemoteEndPoint?.ToString()}.");

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
                Logger.LogError(ode, $"The server {Name} socket was disposed before the Read operation completed on {IpAddress}:{Port}.");
            }
            catch (OperationCanceledException)
            {
                StopToken.Dispose();
                StopToken = new CancellationTokenSource();
                EventHub.Publish(new StartMessage(UniqueIdentifier, true));
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error while processing request from {Client?.Client?.RemoteEndPoint?.ToString()} on {Name} at {IpAddress}:{Port}.");
                OnError(ex);
            }

            return new Memory<byte>(data);
        }

        protected virtual void OnData(Memory<byte> data)
        {
            Logger.LogInformation($"Server {Name} received message from client at server listening on {IpAddress}:{Port} that is [{data.Length}] bytes long.");
            EventHub.Publish(new DataMessage(UniqueIdentifier, data));
        }

        protected virtual IPEndPoint GetServerAddress()
        {
            return new IPEndPoint(IpAddress, Port);
        }
    }
}
