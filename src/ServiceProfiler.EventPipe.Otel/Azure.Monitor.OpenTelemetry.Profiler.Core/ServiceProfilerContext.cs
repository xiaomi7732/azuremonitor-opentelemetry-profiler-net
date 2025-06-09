using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal class ServiceProfilerContext : ServiceProfilerContextBase
{
    private readonly ServiceProfilerOptions _options;

    public ServiceProfilerContext(
        IEndpointProvider endpointProvider,
        IConnectionStringParserFactory connectionStringParserFactory,
        IOptions<ServiceProfilerOptions> options,
        ILogger<ServiceProfilerContext> logger)
        : base(connectionStringParserFactory, endpointProvider, logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public override string ConnectionString => GetRequiredConnectionString(_options);

    private string GetRequiredConnectionString(ServiceProfilerOptions options)
    {
        string? connectionString = options.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string is required. Please make sure its properly set.");
        }
        return connectionString;
    }
}