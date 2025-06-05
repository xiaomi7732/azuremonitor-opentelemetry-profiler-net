namespace Microsoft.ApplicationInsights.Profiler.Core.IPC
{
    internal interface INamedPipeServerFactory
    {
        INamedPipeServerService CreateNamedPipeService();
    }
}
