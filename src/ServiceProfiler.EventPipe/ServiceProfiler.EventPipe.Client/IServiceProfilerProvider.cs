//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.ServiceProfiler.Orchestration;

namespace Microsoft.ApplicationInsights.Profiler.Core;

internal interface IServiceProfilerProvider
{
    Task<bool> StartServiceProfilerAsync(IProfilerSource source, CancellationToken cancellationToken = default);

    Task<bool> StopServiceProfilerAsync(IProfilerSource source, CancellationToken cancellationToken = default);
}
