using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Microsoft.Extensions.Logging;
using Xunit;
using PubSub;
using Microsoft.Extensions.DependencyInjection;
using coder.net.core;
using coder.net.transport;
using coder.net.configuration;
using coder.net.core.pubsub.messages;
using coder.net.application;

namespace coder.net.tests
{
    public class ServerTests
	{
		private const string TestData = "this is a major victory!";

		private readonly Hub _hub = Hub.Default;

		private readonly ILoggerFactory _loggerFactory;
		private readonly ServerConfiguration _config;

		public ServerTests()
		{
			var factory = new Mock<ILoggerFactory>();
			_loggerFactory = new LoggerFactory();
			_config = new ServerConfiguration()
			{
				Name = "Server",
				IpAddress = "127.0.0.1",
				ConnectionCount = 1,
				Port = 8084,
				KeepConnectionOpen = true
			};
			var services = ConfigureServices();
		}

		[Fact]
		public async Task ShouldReceiveMessage()
		{
			ITcpServer sut;
			byte[] buffer;
			Memory<byte> data = null;

			using (var are = new AutoResetEvent(false))
			{
				using (sut = new TcpServer(_loggerFactory, _config))
				{
					buffer = new byte[1024];
					buffer.Initialize();

					_hub.Subscribe<DataMessage>((message) =>
					{
						data = message.Payload;
						are.Set();
					});

					var mainThread = Task.Run(async () =>
					{
						await sut.Run();

						are.Set();
					}
					).ConfigureAwait(false);

					await Task.Run(async () =>
					{
						var timeout = 10;
                        while(sut.Stopped)
                        {
							await Task.Delay(1000);
                            if (timeout-- <= 0)
                            {
								break;
                            }
                        }

						await Task.Delay(5000);

						Buffer.BlockCopy(Encoding.ASCII.GetBytes(TestData), 0, buffer, 0, TestData.Length);
						var client = new TcpClient(AddressFamily.InterNetwork);
						await client.ConnectAsync(IPAddress.Parse("127.0.0.1"), 8084);
						var safeStream = Stream.Synchronized(client.GetStream());

						// NOTE: we don't use ConfigureAwait(false) inside a Task.Run becasue of this article:
						// https://devblogs.microsoft.com/dotnet/configureawait-faq/

						await safeStream.WriteAsync(buffer, 0, buffer.Length);
					}).ConfigureAwait(false);

					Assert.True(are.WaitOne(timeout: TimeSpan.FromSeconds(4), true));
					are.Reset();

					Assert.Equal(buffer, data.ToArray());

					sut.Stop();
				}

				Assert.True(are.WaitOne(timeout: TimeSpan.FromSeconds(4), true));
			}

			Console.WriteLine($"Message sent and received...");
		}

		private ServiceProvider ConfigureServices()
		{
			var config = new Mock<IServerConfiguration>();
			IServiceCollection services = new ServiceCollection();
			services
				.AddTransient<ITcpServer, TcpServer>()
				.AddTransient<IController, Controller>()
				.AddSingleton<IServerConfiguration, ServerConfiguration>()
				.AddLogging(configure => configure.AddConsole());

			return services.BuildServiceProvider();
		}
	}
}
