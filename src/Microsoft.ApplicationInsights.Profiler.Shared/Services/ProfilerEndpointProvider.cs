using System;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Contract;
using ServiceProfiler.Common.Utilities;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal class ProfilerEndpointProvider : IEndpointProvider
{
    private readonly UserConfigurationBase _options;
    private readonly ConnectionString? _connectionString;

    public ProfilerEndpointProvider(
        ConnectionString connectionString,
        IOptions<UserConfigurationBase> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _connectionString = connectionString;
    }

    public Uri GetEndpoint()
    {
        // First priority of user overwrites.
        if (_options.Endpoint is not null)
        {
            return new Uri(_options.Endpoint, UriKind.Absolute);
        }

        if (_connectionString is not null)
        {
            // Second priority is connection string.
            return _connectionString.ResolveProfilerEndpoint();
        }
        
        // Last priority is default endpoint.
        return new Uri(FrontendEndpoints.ProdGlobal, UriKind.Absolute);
    }
}