using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Utilities;

namespace Microsoft.ApplicationInsights.Profiler.Core.TraceScavenger;

internal class TraceScavengerService : BackgroundService
{
    private readonly ILogger _logger;
    private readonly TraceScavengerServiceOptions _options;
    private readonly FileScavenger _fileScavenger;

    public TraceScavengerService(
        IOptions<UserConfiguration> userConfiguration,
        FileScavenger fileScavenger,
        ILogger<TraceScavengerService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _options = userConfiguration?.Value?.TraceScavenger ?? throw new ArgumentNullException(nameof(userConfiguration));
        _fileScavenger = fileScavenger ?? throw new ArgumentNullException(nameof(fileScavenger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan initialDelay = _options.InitialDelay;
        _logger.LogInformation("{serviceName} started. Initial delay: {delay}, Grace period from last access: {gracePeriod}", nameof(TraceScavengerService), initialDelay, _options.GracePeriod);
        await Task.Delay(initialDelay, stoppingToken).ConfigureAwait(false);

        while (true)
        {
            _fileScavenger.Run(stoppingToken);
            await Task.Delay(_options.Interval, stoppingToken).ConfigureAwait(false);
        }
    }
}
