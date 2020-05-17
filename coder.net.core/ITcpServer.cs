using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using coder.net.core.common;

namespace coder.net.core
{
    public interface ITcpServer : IDisposable
    {
		Id UniqueIdentifier { get; }

		string Name { get; }

		bool Stopped { get; }

		TcpClient Client { get; }

		Task<bool> Run();

		bool Stop();

		Task Restart();
    }
}
