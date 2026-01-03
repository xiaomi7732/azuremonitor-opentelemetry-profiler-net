//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

/// <summary>
/// A bootstrap to use when profiler is disabled.
/// This prevents the DI container from creating any service profiler related dependencies.
/// </summary>
internal class DisabledAgentBootstrap : IServiceProfilerAgentBootstrap
{
    private readonly BootstrapState _bootstrapState;
    private readonly ILogger _logger;

    public DisabledAgentBootstrap(
        BootstrapState bootstrapState,
        ILogger<DisabledAgentBootstrap> logger)
    {
        _bootstrapState = bootstrapState ?? throw new ArgumentNullException(nameof(bootstrapState));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task ActivateAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Service Profiler is disabled by user configuration.");
        _bootstrapState.SetProfilerRunning(false);
        return Task.CompletedTask;
    }
}
