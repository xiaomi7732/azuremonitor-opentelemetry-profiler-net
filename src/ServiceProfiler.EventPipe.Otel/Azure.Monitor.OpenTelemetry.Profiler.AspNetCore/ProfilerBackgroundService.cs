using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.AspNetCore;

internal class ProfilerBackgroundService : BackgroundService
{
    private readonly IServiceProfilerAgentBootstrap _bootstrap;
    private readonly ILogger<ProfilerBackgroundService> _logger;

    public ProfilerBackgroundService(
        IServiceProfilerAgentBootstrap bootstrap,
        ILogger<ProfilerBackgroundService> logger)
    {
        _bootstrap = bootstrap ?? throw new ArgumentNullException(nameof(bootstrap));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Triggering Profiler bootstrap");
        // Reduce chance to block the startup of the host. Refer to https://github.com/dotnet/runtime/issues/36063 for more details.
        await Task.Yield();
        await _bootstrap.ActivateAsync(stoppingToken).ConfigureAwait(false);

        _logger.LogInformation("Profiler bootstrap triggered.");

        // IProfilerSource profilerSource = _serviceProfilerProvider as IProfilerSource ?? UnknownProfilerSource.Instance;
        // _logger.LogInformation("Start profiler service...");
        // await _serviceProfilerProvider.StartServiceProfilerAsync(profilerSource, stoppingToken).ConfigureAwait(false);

        // await Task.Delay(_options.Duration, cancellationToken: stoppingToken).ConfigureAwait(false);

        // await _serviceProfilerProvider.StopServiceProfilerAsync(profilerSource, stoppingToken).ConfigureAwait(false);
        // _logger.LogInformation("Done");
    }
}