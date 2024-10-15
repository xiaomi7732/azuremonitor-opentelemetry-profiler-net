using Azure.Monitor.OpenTelemetry.Profiler.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Monitor.OpenTelemetry.Profiler.AspNetCore;

internal class ProfilerBackgroundService : BackgroundService
{
    private readonly IServiceProfilerProvider _serviceProfilerProvider;
    private readonly ServiceProfilerOptions _options;
    private readonly ILogger<ProfilerBackgroundService> _logger;

    public ProfilerBackgroundService(
      IServiceProfilerProvider serviceProfilerProvider,
      IOptions<ServiceProfilerOptions> options,
      ILogger<ProfilerBackgroundService> logger)
    {
        _serviceProfilerProvider = serviceProfilerProvider ?? throw new ArgumentNullException(nameof(serviceProfilerProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IProfilerSource profilerSource = _serviceProfilerProvider as IProfilerSource ?? UnknownProfilerSource.Instance;
        _logger.LogInformation("Start profiler service...");
        await _serviceProfilerProvider.StartServiceProfilerAsync(profilerSource, stoppingToken).ConfigureAwait(false);

        await Task.Delay(_options.Duration, cancellationToken: stoppingToken).ConfigureAwait(false);

        await _serviceProfilerProvider.StopServiceProfilerAsync(profilerSource, stoppingToken).ConfigureAwait(false);
        _logger.LogInformation("Done");
    }
}