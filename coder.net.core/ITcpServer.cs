using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace coder.net.core
{
    public interface ITcpServer
    {
		string Name { get; }

		bool Stopped { get; }

		TcpClient Client { get; }

		Task RunServer();

		void StopServer();

		Task Restart();

		// EventContext Context { get; }
	}
}
