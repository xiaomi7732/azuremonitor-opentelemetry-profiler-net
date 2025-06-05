//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Profiler.Core;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.Extensions.Options;
using System;
using System.Reflection;

namespace Microsoft.ApplicationInsights.Profiler.AspNetCore
{
    /// <summary>
    /// Reflects the EndpointProvider in AI SDK.
    /// This is used as the connection string parser to get instrumentation key
    /// and various endpoints.
    /// </summary>
    internal class EndpointProviderMirror : IEndpointProvider
    {
        private PropertyInfo _connectionStringProperty;
        private MethodInfo _getInstrumentationKeyMethod;
        private MethodInfo _getEndpointMethod;
        private object _endpointProvider;

        public EndpointProviderMirror(IOptions<TelemetryConfiguration> customerTelemetryConfigurationOptions)
        {
            Type endpointProviderType = Initialize();
            _endpointProvider = Activator.CreateInstance(endpointProviderType);

            string customerConnectionString = customerTelemetryConfigurationOptions.Value?.ConnectionString;
            if (!string.IsNullOrEmpty(customerConnectionString))
            {
                ConnectionString = customerConnectionString;
            }
        }

        public string ConnectionString
        {
            get
            {
                return (string)_connectionStringProperty.GetValue(_endpointProvider);
            }
            set
            {
                _connectionStringProperty.SetValue(_endpointProvider, value);
            }
        }

        public string GetInstrumentationKey()
        {
            if (ConnectionString == null)
            {
                return null;
            }

            return (string)_getInstrumentationKeyMethod.Invoke(_endpointProvider, null);
        }

        public Uri GetEndpoint(EndpointName endpointName)
        {
            return (Uri)_getEndpointMethod.Invoke(_endpointProvider, new object[] { endpointName });
        }

        private Type Initialize()
        {
            Assembly applicationInsights = Assembly.GetAssembly(typeof(Microsoft.ApplicationInsights.TelemetryClient));
            if (applicationInsights == null)
            {
                throw new NullReferenceException("Can't find Microsoft.ApplicationInsights assembly");
            }

            Type endpointProviderType = applicationInsights.GetType("Microsoft.ApplicationInsights.Extensibility.Implementation.Endpoints.EndpointProvider", throwOnError: true);
            _connectionStringProperty = endpointProviderType.GetProperty(nameof(ConnectionString));
            _getInstrumentationKeyMethod = endpointProviderType.GetMethod(nameof(GetInstrumentationKey));
            _getEndpointMethod = endpointProviderType.GetMethod(nameof(GetEndpoint));

            return endpointProviderType;
        }
    }
}