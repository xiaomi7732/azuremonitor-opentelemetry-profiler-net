using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal class CustomEventsTracker : ICustomEventsTracker
{
    private readonly ILogger _logger;

    public CustomEventsTracker(ILogger<CustomEventsTracker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int Send(IEnumerable<SampleActivity> samples, UploadContextModel uploadContext, int processId, string profilingSource, Guid verifiedDataCube)
    {
        _logger.LogWarning("Sending Custom Events are not implemented yet.");
        return samples.Count();
    }
}