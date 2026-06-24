namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.IPC;

/// <summary>
/// Exception thrown when the named pipe is closed by the peer before a message could be read.
/// This typically means the other process (for example, the trace uploader) exited prematurely.
/// Check that process's logs for the underlying failure.
/// </summary>
internal class NamedPipeClosedException : System.Exception
{
    public NamedPipeClosedException() { }
    public NamedPipeClosedException(string message) : base(message) { }
    public NamedPipeClosedException(string message, System.Exception inner) : base(message, inner) { }
}
