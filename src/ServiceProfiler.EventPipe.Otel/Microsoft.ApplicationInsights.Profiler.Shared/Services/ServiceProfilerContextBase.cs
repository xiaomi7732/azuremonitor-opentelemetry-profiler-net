using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Contract;
using Microsoft.ServiceProfiler.Utilities;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal abstract class ServiceProfilerContextBase : IServiceProfilerContext
{
    private readonly IEndpointProvider _endpointProvider;
    private readonly ILogger _logger;

    public ServiceProfilerContextBase(
        IEndpointProvider endpointProvider,
        ILogger<ServiceProfilerContextBase> logger
    )
    {
        _endpointProvider = endpointProvider ?? throw new ArgumentNullException(nameof(endpointProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        StampFrontendEndpointUrl = _endpointProvider.GetEndpoint();
        if (StampFrontendEndpointUrl != new Uri(FrontendEndpoints.ProdGlobal, UriKind.Absolute))
        {
            _logger.LogWarning("Custom endpoint: {endpoint}. This is not supposed to be used in production. File an issue if this is not intended.", StampFrontendEndpointUrl);
        }
    }

    public string MachineName => EnvironmentUtilities.MachineName;

    public Uri StampFrontendEndpointUrl { get; }

    public abstract Guid AppInsightsInstrumentationKey { get; }
    public abstract string ConnectionString { get; }
}