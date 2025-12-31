using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

/// <summary>
/// Base class for background services that depend on Profiler running.
/// If the Profiler is not running, the service will not start.
/// Profiler might not be running due to user configuration or incompatible environment.
/// For example, Profiler is disabled when the connection string is not provided.
/// </summary>
internal abstract class DependantBackgroundServiceBase : BackgroundService
{
    private readonly BootstrapState _bootstrapState;
    private readonly ILogger<DependantBackgroundServiceBase> _logger;

    public DependantBackgroundServiceBase(BootstrapState bootstrapState, ILogger<DependantBackgroundServiceBase> logger)
    {
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
        _bootstrapState = bootstrapState ?? throw new System.ArgumentNullException(nameof(bootstrapState));
    }

    protected abstract Task ExecuteAfterProfilerBootstrapAsync(bool isProfilerBootstrapped, CancellationToken stoppingToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        _logger.LogTrace("Waiting for bootstrap state ...");
        bool isProfilerBootstrapped =  _bootstrapState.IsProfilerRunning(stoppingToken);
        _logger.LogDebug("Bootstrap state ready. Is profiler bootstrapped: {isProfilerBootstrapped}", isProfilerBootstrapped);

        await ExecuteAfterProfilerBootstrapAsync(isProfilerBootstrapped,stoppingToken).ConfigureAwait(false);
    }
}