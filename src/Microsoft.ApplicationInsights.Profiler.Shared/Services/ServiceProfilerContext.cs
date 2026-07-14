using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Contract;
using Microsoft.ServiceProfiler.Utilities;
using ServiceProfiler.Common.Utilities;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal class ServiceProfilerContext : IServiceProfilerContext
{
    private readonly IEndpointProvider _endpointProvider;
    private readonly ILogger _logger;
    private readonly string? _connectionStringValue;

    public ServiceProfilerContext(
        ConnectionString? connectionString,
        string? connectionStringValue,
        IEndpointProvider endpointProvider,
        ILogger<ServiceProfilerContext> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Allow null connection string for local development scenarios.
        ConnectionString = connectionString;
        _connectionStringValue = connectionStringValue;
        ConnectionStringValidation = ValidateConnectionString();
        
        _endpointProvider = endpointProvider ?? throw new ArgumentNullException(nameof(endpointProvider));

        StampFrontendEndpointUrl = _endpointProvider.GetEndpoint();
        if (StampFrontendEndpointUrl != new Uri(FrontendEndpoints.ProdGlobal, UriKind.Absolute))
        {
            _logger.LogWarning("Custom endpoint: {endpoint}. This is not supposed to be used in production. File an issue if this is not intended.", StampFrontendEndpointUrl);
        }
    }

    public string MachineName => EnvironmentUtilities.MachineName;

    public Uri StampFrontendEndpointUrl { get; }

    public Guid AppInsightsInstrumentationKey => ConnectionString is null ? Guid.Empty : ConnectionString.InstrumentationKeyGuid;

    public ConnectionString? ConnectionString { get; }

    public ConnectionStringValidationResult ConnectionStringValidation { get; }

    private ConnectionStringValidationResult ValidateConnectionString()
    {
        if (_connectionStringValue is null)
        {
            return ConnectionStringValidationResult.NotConfigured;
        }

        if (string.IsNullOrWhiteSpace(_connectionStringValue))
        {
            return ConnectionStringValidationResult.Empty;
        }

        // Connection string is present but could not be parsed.
        if (ConnectionString is null)
        {
            // Distinguish an invalid instrumentation key (an "InstrumentationKey" token that is
            // empty/whitespace or not a GUID, e.g. "InstrumentationKey=" or "InstrumentationKey=not-a-guid")
            // from an otherwise malformed connection string. The former yields a more actionable error.
            return ContainsInvalidInstrumentationKey(_connectionStringValue)
                ? ConnectionStringValidationResult.InvalidInstrumentationKey
                : ConnectionStringValidationResult.Malformed;
        }

        // Connection string parsed, but the instrumentation key is missing or malformed.
        if (AppInsightsInstrumentationKey == Guid.Empty)
        {
            return ConnectionStringValidationResult.InvalidInstrumentationKey;
        }

        return ConnectionStringValidationResult.Valid;
    }

    /// <summary>
    /// Determines whether the raw connection string declares any <c>InstrumentationKey</c> token
    /// whose value is invalid - that is, empty/whitespace or not a valid <see cref="Guid"/>.
    /// </summary>
    private static bool ContainsInvalidInstrumentationKey(string connectionStringValue)
    {
        foreach (string token in connectionStringValue.Split(';'))
        {
            int separatorIndex = token.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            string key = token.Substring(0, separatorIndex).Trim();
            if (!key.Equals("InstrumentationKey", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string value = token.Substring(separatorIndex + 1);
            if (string.IsNullOrWhiteSpace(value) || !Guid.TryParse(value.Trim(), out _))
            {
                return true;
            }
        }

        return false;
    }
}