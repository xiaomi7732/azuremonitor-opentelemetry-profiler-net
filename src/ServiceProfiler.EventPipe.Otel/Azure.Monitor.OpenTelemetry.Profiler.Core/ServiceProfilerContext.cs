using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal class ServiceProfilerContext : ServiceProfilerContextBase
{
    private readonly IConnectionStringParserFactory _connectionStringParserFactory;
    private readonly ServiceProfilerOptions _options;

    public ServiceProfilerContext(
        IEndpointProvider endpointProvider,
        IConnectionStringParserFactory connectionStringParserFactory,
        IOptions<ServiceProfilerOptions> options,
        ILogger<ServiceProfilerContext> logger)
        : base(endpointProvider, logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _connectionStringParserFactory = connectionStringParserFactory ?? throw new ArgumentNullException(nameof(connectionStringParserFactory));
    }

    public override Guid AppInsightsInstrumentationKey => GetApplicationInsightsInstrumentationKey(_options);

    public override string ConnectionString => GetRequiredConnectionString(_options);

    private Guid GetApplicationInsightsInstrumentationKey(ServiceProfilerOptions options)
    {
        IConnectionStringParser connectionStringParser = _connectionStringParserFactory.Create(GetRequiredConnectionString(options));
        if (connectionStringParser.TryGetValue(ConnectionStringParser.Keys.InstrumentationKey, out string? instrumentationKeyValue))
        {
            return Guid.Parse(instrumentationKeyValue!);
        }
        throw new InvalidOperationException("Instrumentation key does not exist in the connection string.");
    }

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