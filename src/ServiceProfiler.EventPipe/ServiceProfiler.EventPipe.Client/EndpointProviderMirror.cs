//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.Reflection;

namespace Microsoft.ApplicationInsights.Profiler.Core;

/// <summary>
/// Reflects the EndpointProvider in AI SDK.
/// This is used as the connection string parser to get instrumentation key
/// and various endpoints.
/// </summary>
internal class EndpointProviderMirror : IEndpointProvider
{
    private MethodInfo _getEndpointMethod;
    private object _endpointProvider;

    public EndpointProviderMirror(IOptions<TelemetryConfiguration> customerTelemetryConfigurationOptions)
    {
        Type endpointProviderType = Initialize();
        _endpointProvider = Activator.CreateInstance(endpointProviderType);
    }

    public Uri GetEndpoint()
    {
        return (Uri)_getEndpointMethod.Invoke(_endpointProvider, new object[] { "Profiler" });
    }

    private Type Initialize()
    {
        Assembly applicationInsights = Assembly.GetAssembly(typeof(Microsoft.ApplicationInsights.TelemetryClient));
        if (applicationInsights == null)
        {
            throw new NullReferenceException("Can't find Microsoft.ApplicationInsights assembly");
        }

        Type endpointProviderType = applicationInsights.GetType("Microsoft.ApplicationInsights.Extensibility.Implementation.Endpoints.EndpointProvider", throwOnError: true);
        _getEndpointMethod = endpointProviderType.GetMethod(nameof(GetEndpoint));

        return endpointProviderType;
    }
}