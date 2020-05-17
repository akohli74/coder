using System;
using System.Threading.Tasks;
using coder.net.core;
using coder.net.core.common;
using coder.net.core.pubsub.messages;
using Microsoft.Extensions.Logging;
using PubSub;

namespace coder.net.console
{
    public class Bootstrapper
    {
        protected ILogger Logger { get; set; }

        private IController _controller;

        public Bootstrapper(ILoggerFactory loggerFactory, IController controller)
        {
            Logger = loggerFactory?.CreateLogger<Bootstrapper>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public async Task Run()
        {
            await _controller.Run();
        }
    }
}
