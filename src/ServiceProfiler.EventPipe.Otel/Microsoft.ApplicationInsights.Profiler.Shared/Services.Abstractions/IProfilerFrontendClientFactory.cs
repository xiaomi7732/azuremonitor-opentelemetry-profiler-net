using Microsoft.ServiceProfiler.Agent.FrontendClient;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface IProfilerFrontendClientFactory
{
    IProfilerFrontendClient CreateProfilerFrontendClient();
}
