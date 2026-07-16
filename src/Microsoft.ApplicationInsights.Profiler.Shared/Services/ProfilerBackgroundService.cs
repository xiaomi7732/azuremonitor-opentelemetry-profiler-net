// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

/// <summary>
/// A background service to trigger profiler bootstrap.
/// </summary>
internal class ProfilerBackgroundService : BackgroundService
{
    private readonly IServiceProfilerAgentBootstrap _bootstrap;
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly UserConfigurationBase _userConfiguration;
    private readonly ILogger<ProfilerBackgroundService> _logger;

    public ProfilerBackgroundService(
        IServiceProfilerAgentBootstrap bootstrap,
        IServiceProfilerContext serviceProfilerContext,
        IOptions<UserConfigurationBase> userConfiguration,
        ILogger<ProfilerBackgroundService> logger)
    {
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _userConfiguration = userConfiguration?.Value ?? throw new ArgumentNullException(nameof(userConfiguration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("Triggering Profiler bootstrap ...");
        // Reduce chance to block the startup of the host. Refer to https://github.com/dotnet/runtime/issues/36063 for more details.
        await Task.Yield();
        await _bootstrap.ActivateAsync(stoppingToken).ConfigureAwait(false);

        _logger.LogDebug("Profiler bootstrap finished.");
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Emit the connection-string diagnostic synchronously here (before ExecuteAsync), so a clear,
        // actionable message is logged as early as possible - even if something else faults host
        // startup shortly after, for example the Azure Monitor exporter, which throws while building its
        // telemetry providers when no connection string is configured. This runs on the host-startup
        // thread, and this profiler background service is not always wrapped in a fail-safe host-service
        // wrapper (the classic profiler registers it directly), so it must never throw.
        LogConnectionStringDiagnostic();
        return base.StartAsync(cancellationToken);
    }

    private void LogConnectionStringDiagnostic()
    {
        try
        {
            // Only warn about the connection string when the profiler would otherwise start. When it is
            // disabled by configuration, a missing connection string is irrelevant to the profiler, and
            // an error here would be misleading (the profiler is off, not misconfigured).
            if (_userConfiguration.IsDisabled)
            {
                return;
            }

            string? error = ConnectionStringDiagnostics.GetConfigurationError(_serviceProfilerContext.ConnectionStringValidation);
            if (error is not null)
            {
                _logger.LogError(
                    "{error}{profilerWontStart}{exporterHint}",
                    error,
                    ConnectionStringDiagnostics.ProfilerWontStartSuffix,
                    ConnectionStringDiagnostics.ExporterConnectionStringHint);
            }
        }
        catch (Exception ex)
        {
            // Diagnostics must never fault host startup.
            _logger.LogDebug(ex, "Failed to evaluate the connection-string diagnostic at startup.");
        }
    }
}