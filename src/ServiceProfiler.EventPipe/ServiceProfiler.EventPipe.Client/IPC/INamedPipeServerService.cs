using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Core.IPC
{

    /// <summary>
    /// A service to run as a named pipe server.
    /// </summary>
    public interface INamedPipeServerService : INamedPipeOperations, IDisposable
    {
        /// <summary>
        /// Start a named pipe and wait for connection.
        /// </summary>
        Task WaitForConnectionAsync(string pipeName, CancellationToken cancellationToken);
    }
}
