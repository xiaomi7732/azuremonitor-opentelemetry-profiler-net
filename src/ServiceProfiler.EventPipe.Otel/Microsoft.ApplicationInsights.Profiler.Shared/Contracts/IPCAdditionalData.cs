using System.Collections.Generic;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Contracts;

internal record IPCAdditionalData
{
    public ServiceProfilerIndex ServiceProfilerIndex { get; init; } = default!;

    public IEnumerable<ServiceProfilerSample> ServiceProfilerSamples { get; init; } = default!;
}