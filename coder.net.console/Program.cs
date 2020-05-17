using System.Threading.Tasks;
using coder.net.app;
using coder.net.configuration;
using coder.net.core;
using coder.net.transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace coder.net.console
{
    class Program
    {
        static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        static async Task MainAsync()
        {
            var serviceProvider = ConfigureServices();

            await serviceProvider.GetService<Bootstrapper>().Run();

            serviceProvider.Dispose();
        }

        static IServerConfiguration BuildConfiguration()
        {
            var configBuilder = new ConfigurationBuilder();
            configBuilder.AddJsonFile("config.json");
            var config = configBuilder.Build();
            return config.Get<ServerConfiguration>();
        }

        static ServiceProvider ConfigureServices()
        {
            var config = BuildConfiguration();
            IServiceCollection services = new ServiceCollection();
            services
                .AddTransient<ITcpServer, TcpServer>()
                .AddTransient<IController, Controller>()
                .AddSingleton<IServerConfiguration, ServerConfiguration>()
                .AddTransient<Bootstrapper>()
                .AddLogging(configure => configure.AddConsole());

            return services.BuildServiceProvider();
        }
    }
}
