using System;
using System.Threading;
using System.Threading.Tasks;
using coder.net.core.common;
using coder.net.core.pubsub.messages;
using Microsoft.Extensions.Logging;
using PubSub;

namespace coder.net.core.threading
{
    public class RunningTask : IDisposable
    {
        public Id UniqueIdentifier { get; } = new Id();
        public string Name { get; protected set; }
        public bool Stopped { get; set; } = true;

        protected CancellationTokenSource StopToken { get; set; }
        protected ILogger<RunningTask> Logger { get; set; }
        protected bool Restarting { get; set; } = false;
        protected Hub EventHub = null;
        protected bool Disposed = false;

        public RunningTask(ILoggerFactory loggerFactory)
        {
            Logger = loggerFactory?.CreateLogger<RunningTask>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            EventHub = Hub.Default ?? throw new InvalidOperationException($"The PubSub Hub does not have a default hub - {nameof(EventHub)}");

            StopToken = new CancellationTokenSource();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            try
            {
                if (Disposed)
                {
                    return;
                }

                if (disposing)
                {
                    StopToken.Dispose();
                }

                Disposed = true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Disposing of the Runnable Task with Id {UniqueIdentifier} and Name {Name} has caused an exception.");
                throw;
            }
        }

        public virtual async Task Restart()
        {
            try
            {
                Restarting = true;

                Logger.LogWarning($"Restarting Task with Id {UniqueIdentifier} and Name {Name}.");

                if (!Stopped)
                {
                    StopToken.Cancel();

                    await this.Timeout(5);

                    StopToken.Dispose();
                    StopToken = new CancellationTokenSource();
                }

                if (Stopped)
                {
                    EventHub.Publish(new StartMessage(UniqueIdentifier, true));
                }

            }
            catch (OperationCanceledException oce)
            {
                Logger.LogWarning(oce, $"Task running with Id {UniqueIdentifier} and Name {Name} is stopping.  Cannot restart a stopping Task.");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Exception while restarting Task with Id {UniqueIdentifier} and Name {Name}.");
                EventHub.Publish(new StartMessage(UniqueIdentifier, true));
            }

            Restarting = false;
        }

        public virtual async Task<bool> Run()
        {
            return await Task.FromResult(true);
        }

        public override string ToString()
        {
            return $"{Name}-{UniqueIdentifier}";
        }

        protected virtual async Task RunAsync()
        {
            await Task.FromCanceled(StopToken.Token);
        }

        protected virtual async Task SpawnProcess()
        {
            await Task.Factory.StartNew(
                            async () =>
                            {
                                if (Stopped)
                                {
                                    Stopped = false;

                                    Logger.LogInformation($"Starting process with UniqueIdentifier {UniqueIdentifier} and Name {Name}.");

                                    await RunAsync();

                                    Stopped = true;
                                }

                            }, StopToken.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap().ConfigureAwait(false);
        }

        public virtual bool Stop()
        {
            StopToken?.Cancel();
            return true;
        }

        protected void OnError(Exception ex)
        {
            EventHub.Publish(new ErrorMessage(UniqueIdentifier, ex));
        }

        protected async Task Timeout(short seconds)
        {
            await Task.Delay(seconds * 1000, StopToken.Token);
        }
    }
}
