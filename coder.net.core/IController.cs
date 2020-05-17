using System;
using System.Threading.Tasks;

namespace coder.net.core
{
    public interface IController
    {
        Task<bool> Run();

        bool Stop();

        Task Restart();
    }
}
