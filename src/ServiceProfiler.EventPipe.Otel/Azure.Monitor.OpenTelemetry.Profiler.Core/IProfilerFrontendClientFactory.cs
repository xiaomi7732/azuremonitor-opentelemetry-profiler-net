using Microsoft.ServiceProfiler.Agent.FrontendClient;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal interface IProfilerFrontendClientFactory
{
    IProfilerFrontendClient CreateProfilerFrontendClient();
}