using System;
using System.Threading.Tasks;
using coder.net.core;

namespace coder.net.console
{
    public class Bootstrapper
    {
        private ITcpServer Server { get; set; }

        public Bootstrapper(ITcpServer server)
        {
            Server = server ?? throw new ArgumentNullException(nameof(server));
        }

        public async Task Run()
        {
            await Server.RunServer();
        }
    }
}
