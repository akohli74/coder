using System;
using System.Threading;
using System.Threading.Tasks;
using coder.net.core;
using coder.net.core.pubsub.messages;
using coder.net.core.threading;
using Microsoft.Extensions.Logging;
using PubSub;

namespace coder.net.app
{
    public class Controller : RunningTask, IController
    {
        protected ITcpServer Server { get; private set; }

        private Hub _hub;

        public Controller(ILoggerFactory loggerFactory, ITcpServer server)
            : base(loggerFactory)
        {
            Server = server ?? throw new ArgumentNullException(nameof(server));
            Logger = loggerFactory?.CreateLogger<Controller>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            _hub = Hub.Default ?? throw new InvalidOperationException($"The PubSub Hub does not have a default hub - {nameof(Hub)}");

            StopToken = new CancellationTokenSource();
        }

        public async override Task<bool> Run()
        {
            BootstrapEvents();
            return await Server.Run().ConfigureAwait(false);
        }

        private void BootstrapEvents()
        {
            _hub.Subscribe<StartMessage>(this, OnStart);
            _hub.Subscribe<DataReceivedMessage>(this, OnDataReceived);
            _hub.Subscribe<ErrorMessage>(this, OnError);
        }

        private async Task OnStart(StartMessage message)
        {
            _ = message?.MessageId switch
            {
                var id when id.Equals(Server.UniqueIdentifier) => await StartServer(message.Start),
                null => throw new ArgumentException(message: $"Invalid Message: A message must have a Unique Id.  This message has no Id."),
                _ => throw new ArgumentException(message: $"Unhandled Message: A message with an unrecognized Id has been received.  The Id of this message is {message?.MessageId}.")
            };
        }

        private async Task<bool> StartServer(bool start)
        {
            return start switch
            {
                true => await Server.Run(),
                _ => Server.Stop()
            };
        }

        private void OnDataReceived(DataReceivedMessage message)
        {
            Logger.LogInformation($"The server with id {Server.UniqueIdentifier} has received data from a remote client.");
        }

        private void OnError(ErrorMessage message)
        {
            // TODO: Handle/Notify errors
        }
    }
}
