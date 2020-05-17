using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using coder.net.core;
using Microsoft.Extensions.Logging;

namespace coder.net.transport
{
    public class TcpServer : ITcpServer
    {
        public TcpClient Client { get; protected set; }
        public string Name { get; }
        public bool Stopped { get; set; }

        protected TcpListener Listener { get; set; }
        protected CancellationTokenSource StopToken { get; set; }

        protected readonly ILogger<TcpServer> _logger;
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
        protected bool Restarting { get; set; } = false;

        private bool _disposed = false;

        public TcpServer(ILoggerFactory loggerFactory, IServerConfiguration config)
        {
            _logger = loggerFactory?.CreateLogger<TcpServer>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Dipose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            try
            {
                if(_disposed)
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

                    StopToken.Dispose();
                }

                _disposed = true;
            }
            catch (SocketException sox)
            {
                _logger.LogError(sox, $"Socket Exception while disposing of the Server.  The Error Code is {sox.ErrorCode}.");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Disposing of the Server has caused an exception.");
                throw;
            }
        }

        public virtual async Task RunServer()
        {
            try
            {
                await SpawnServer().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Stopped = true;
                _logger.LogInformation($"Server on {IpAddress}:{Port} has shut down.  Attempting to restart...");
                StopToken = new CancellationTokenSource();
                // EventAggregator.GetEvent<StartEvent>().Publish(new StartMessage(true, Context));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Thread running Server on {IpAddress}:{Port} has crashed.  Attempting to restart it...");
                // ServerError(ex);
            }
        }

        public virtual void StopServer()
        {
            _logger.LogWarning($"Stopping server on {IpAddress}:{Port}.");
            StopToken.Cancel();
            Listener.Stop();
        }

        public virtual async Task Restart()
        {
            try
            {
                if (!Restarting)
                {
                    _logger.LogWarning($"Restarting server on {IpAddress}:{Port}.");

                    Restarting = true;

                    if (!Stopped)
                    {
                        StopToken.Cancel();
                        Listener?.Stop();
                        await this.Timeout(5);
                    }

                    if (StopToken.IsCancellationRequested)
                    {
                        StopToken.Dispose();
                        StopToken = new CancellationTokenSource();
                    }

                    if (Stopped)
                    {
                        // EventAggregator.GetEvent<StartEvent>().Publish(new StartMessage(true, Context));
                    }

                    Restarting = false;
                }
            }
            catch (OperationCanceledException oce)
            {
                _logger.LogWarning(oce, $"Server listening on {IpAddress}:{Port} is stopping.  Cannot restart a stopping server.");
                Restarting = false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception while restarting server at {IpAddress}:{Port}");
                Restarting = false;
                // EventAggregator.GetEvent<StartEvent>().Publish(new StartMessage(true, Context));
            }
        }

        protected async Task SpawnServer()
        {
            await Task.Factory.StartNew(
                            async () =>
                            {
                                if (Stopped)
                                {
                                    Stopped = false;

                                    _logger.LogInformation($"Starting server on {IpAddress}:{Port}.");

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
                _logger.LogWarning($"Server on {IpAddress}:{Port} is shutting down...");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error while running the Server on {IpAddress}:{Port}...");
                // ServerError(ex);
            }
        }

        protected virtual async Task OpenAndReadAsync()
        {
            while (!Restarting && !Stopped)
            {
                await OpenSocketAsync();
                var data = await ReadAsync();
                // OnData(data);
            }
        }

        protected virtual async Task ReadAndNotifyAsync()
        {
            while (!Restarting && !Stopped)
            {
                var data = await ReadAsync();
                // OnData(data);

                StopToken.Token.ThrowIfCancellationRequested();
            }
        }

        protected virtual async Task<Memory<byte>> ReadAsync()
        {
            _logger.LogDebug($"Accepted connection from {Client?.Client?.RemoteEndPoint}");

            if (RaiseEventOnReceive)
            {
                // var message = new ReceivingMessage(Context);
                // EventAggregator.GetEvent<ReceivingMessageEvent>().Publish(message);
                RaiseEventOnReceive = false;
            }

            var data = await ReceiveAsync();
            return data;
        }

        protected virtual async Task OpenSocketAsync()
        {
            await StartListeningAsync();

            _logger.LogDebug($"Waiting for connection on port {Port}");

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

                _logger.LogInformation($"Server is listening for connections on {IpAddress}:{Port}...");
            }
            catch (SocketException sox)
            {
                _logger.LogError(sox, $"Socket cannot be opened on {IpAddress}:{Port}.");
                if (sox.ErrorCode.Equals(10049))
                {
                    _logger.LogInformation($"There is no client connected to {IpAddress}:{Port}.  Please connect one to continue.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unable to start Server configured to listen on {IpAddress}:{Port}.");
                // ServerError(ex);
            }
        }

        protected async Task<Memory<byte>> ReceiveAsync()
        {
            _logger.LogDebug($"Received connection request from {Client?.Client?.RemoteEndPoint?.ToString()}.");

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
                _logger.LogError(ode, $"The socket was disposed before the Read operation completed on {IpAddress}:{Port}.");
            }
            catch (OperationCanceledException)
            {
                StopToken.Dispose();
                StopToken = new CancellationTokenSource();
                // EventAggregator.GetEvent<StartEvent>().Publish(new StartMessage(true, Context));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error while processing request from {Client?.Client?.RemoteEndPoint?.ToString()} on {IpAddress}:{Port}.");
                // ServerError(ex);
            }

            return new Memory<byte>(data);
        }

        protected virtual IPEndPoint GetServerAddress()
        {
            return new IPEndPoint(IpAddress, Port);
        }

        private async Task Timeout(short seconds)
        {
            await Task.Delay(seconds * 1000, StopToken.Token);
        }

        //protected void ServerError(Exception ex)
        //{
        //    var message = new ExceptionMessage(ex, Context);
        //    EventAggregator.GetEvent<ExceptionEvent>().Publish(message);
        //}
    }
}
