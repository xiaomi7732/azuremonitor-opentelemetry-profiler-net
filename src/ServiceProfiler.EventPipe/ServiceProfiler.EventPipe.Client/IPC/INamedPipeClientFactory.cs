namespace Microsoft.ApplicationInsights.Profiler.Core.IPC
{
    internal interface INamedPipeClientFactory
    {
        INamedPipeClientService CreateNamedPipeService();

    }
}
