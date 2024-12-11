namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.IPC;

internal interface INamedPipeServerFactory
{
    INamedPipeServerService CreateNamedPipeService();
}
