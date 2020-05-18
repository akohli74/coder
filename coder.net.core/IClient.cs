using System;
using System.Threading.Tasks;
using coder.net.core.common;

namespace coder.net.core
{
	public interface IClient : IDisposable
	{
		Id UniqueIdentifier { get; }

		string Name { get; }

		Task<bool> Run();

		Task Restart();

		bool Stop();

		bool QueueCommand(Memory<byte> command);
	}
}
