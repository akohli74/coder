using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using coder.net.core;
using coder.net.core.pubsub.messages;
using coder.net.core.threading;
using Microsoft.Extensions.Logging;
using PubSub;

namespace coder.net.application
{
    public class Controller : RunningTask, IController
    {
        protected ITcpServer Server { get; private set; }
        protected IClient Client { get; private set; }

        private Hub _hub;

        public Controller(ILoggerFactory loggerFactory, ITcpServer server, IClient client)
            : base(loggerFactory)
        {
            Server = server ?? throw new ArgumentNullException(nameof(server));
            Client = client ?? throw new ArgumentNullException(nameof(client));
            _hub = Hub.Default ?? throw new InvalidOperationException($"The PubSub Hub does not have a default hub - {nameof(Hub)}");

            StopToken = new CancellationTokenSource();
        }

        public override async Task<bool> Run()
        {
            try
            {
                Stopped = false;

                var data = $"I'm sending myself to the server...";

                BootstrapEvents();
                _hub.Publish(new StartMessage(Server.UniqueIdentifier, true));
                _hub.Publish(new StartMessage(Client.UniqueIdentifier, true));

                while (!Restarting && !Stopped)
                {
                    await Task.Delay(1000);
                    Client.Send(Encoding.ASCII.GetBytes(data));
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Controller {Name} with Id {UniqueIdentifier} has encountered an error while attempting to start...");
                return false;
            }

            return true;
        }

        private void BootstrapEvents()
        {
            _hub.Subscribe<StartMessage>(this, OnStart);
            _hub.Subscribe<DataReceivedMessage>(this, OnDataReceived);
            _hub.Subscribe<ErrorMessage>(this, OnError);
            _hub.Subscribe<DataMessage>(this, OnData);
        }

        private void OnData(DataMessage data)
        {
            Logger.LogInformation($"Data received from client and it reads as follows: [{Encoding.ASCII.GetString(data.Payload.ToArray())}]");
        }

        private async Task OnStart(StartMessage message)
        {
            _ = message?.MessageId switch
            {
                var id when id.Equals(Server.UniqueIdentifier) => await StartServer(message.Start),
                var id when id.Equals(Client.UniqueIdentifier) => await StartClient(message.Start),
                null => throw new ArgumentException(message: $"Invalid Message: A message must have a Unique Id.  This message has no Id."),
                _ => throw new ArgumentException(message: $"Unhandled Message: A message with an unrecognized Id has been received.  The Id of this message is {message?.MessageId}.")
            };
        }

        private async Task<bool> StartServer(bool start)
        {
            return start switch
            {
                true => await Server.Run().ConfigureAwait(false),
                _ => Server.Stop()
            };
        }

        private async Task<bool> StartClient(bool start)
        {
            return start switch
            {
                true => await Client.Run().ConfigureAwait(false),
                _ => Client.Stop()
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
