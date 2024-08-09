using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.AspNetCore;

internal class ProfilerBackgroundService : BackgroundService
{
  private readonly ILogger<ProfilerBackgroundService> _logger;

  public ProfilerBackgroundService(ILogger<ProfilerBackgroundService> logger)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("Start profiler service...");

    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken: stoppingToken).ConfigureAwait(false);

    _logger.LogInformation("Done");
  }
}