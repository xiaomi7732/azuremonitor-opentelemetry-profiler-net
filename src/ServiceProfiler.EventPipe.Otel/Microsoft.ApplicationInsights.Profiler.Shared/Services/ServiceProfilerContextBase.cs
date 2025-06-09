using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Contract;
using Microsoft.ServiceProfiler.Utilities;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal abstract class ServiceProfilerContextBase : IServiceProfilerContext
{
    private readonly IConnectionStringParserFactory _connectionStringParserFactory;
    private readonly IEndpointProvider _endpointProvider;
    private readonly ILogger _logger;

    public ServiceProfilerContextBase(
        IConnectionStringParserFactory connectionStringParserFactory,
        IEndpointProvider endpointProvider,
        ILogger<ServiceProfilerContextBase> logger
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _endpointProvider = endpointProvider ?? throw new ArgumentNullException(nameof(endpointProvider));
        _connectionStringParserFactory = connectionStringParserFactory ?? throw new ArgumentNullException(nameof(connectionStringParserFactory));

        StampFrontendEndpointUrl = _endpointProvider.GetEndpoint();
        if (StampFrontendEndpointUrl != new Uri(FrontendEndpoints.ProdGlobal, UriKind.Absolute))
        {
            _logger.LogWarning("Custom endpoint: {endpoint}. This is not supposed to be used in production. File an issue if this is not intended.", StampFrontendEndpointUrl);
        }
    }

    public string MachineName => EnvironmentUtilities.MachineName;

    public Uri StampFrontendEndpointUrl { get; }

    public Guid AppInsightsInstrumentationKey => ParseInstrumentationKey();
    
    public abstract string ConnectionString { get; }

    private Guid ParseInstrumentationKey()
    {
        if (_connectionStringParserFactory.Create(ConnectionString).TryGetValue(ConnectionStringParser.Keys.InstrumentationKey, out string? instrumentationKeyValue))
        {
            return Guid.Parse(instrumentationKeyValue);
        }
        throw new InvalidOperationException($"Instrumentation key not found in connection string: {ConnectionString}.");
    }
}