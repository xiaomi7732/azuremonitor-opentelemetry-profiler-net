using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Orchestration;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.Contracts;

internal class ProfilerSettings : ProfilerSettingsBase
{
    public ProfilerSettings(
        IOptions<ServiceProfilerOptions> userConfiguration, 
        IProfilerSettingsService settingsService, 
        ILogger<ProfilerSettingsBase> logger) : base(userConfiguration, settingsService, logger)
    {
    }
}