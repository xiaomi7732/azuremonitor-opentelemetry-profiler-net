using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Contract;
using Microsoft.ServiceProfiler.Utilities;
using ServiceProfiler.Common.Utilities;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal class ServiceProfilerContext : IServiceProfilerContext
{
    private readonly ConnectionString _connectionString;
    private readonly IEndpointProvider _endpointProvider;
    private readonly ILogger _logger;

    public ServiceProfilerContext(
        ConnectionString connectionString,
        IEndpointProvider endpointProvider,
        ILogger<ServiceProfilerContext> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _endpointProvider = endpointProvider ?? throw new ArgumentNullException(nameof(endpointProvider));

        StampFrontendEndpointUrl = _endpointProvider.GetEndpoint();
        if (StampFrontendEndpointUrl != new Uri(FrontendEndpoints.ProdGlobal, UriKind.Absolute))
        {
            _logger.LogWarning("Custom endpoint: {endpoint}. This is not supposed to be used in production. File an issue if this is not intended.", StampFrontendEndpointUrl);
        }

        ConnectionString = _connectionString.ToString();
    }

    public string MachineName => EnvironmentUtilities.MachineName;

    public Uri StampFrontendEndpointUrl { get; }

    public Guid AppInsightsInstrumentationKey => _connectionString.InstrumentationKeyGuid;
    
    public string ConnectionString { get; }
}