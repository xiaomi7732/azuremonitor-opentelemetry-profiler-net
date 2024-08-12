using Azure.Monitor.OpenTelemetry.Profiler.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.AspNetCore;

internal class ProfilerBackgroundService : BackgroundService
{
    private readonly IServiceProfilerProvider _serviceProfilerProvider;
    private readonly ILogger<ProfilerBackgroundService> _logger;

  public ProfilerBackgroundService(
    IServiceProfilerProvider serviceProfilerProvider,
    ILogger<ProfilerBackgroundService> logger)
  {
        _serviceProfilerProvider = serviceProfilerProvider ?? throw new ArgumentNullException(nameof(serviceProfilerProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("Start profiler service...");
    await _serviceProfilerProvider.StartServiceProfilerAsync(null, stoppingToken).ConfigureAwait(false);
    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken: stoppingToken).ConfigureAwait(false);
    
    await _serviceProfilerProvider.StopServiceProfilerAsync(null, stoppingToken).ConfigureAwait(false);
    _logger.LogInformation("Done");
  }
}