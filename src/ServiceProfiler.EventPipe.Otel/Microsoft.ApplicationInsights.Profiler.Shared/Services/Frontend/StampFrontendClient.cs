// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Core.Pipeline;
using Microsoft.ServiceProfiler.Agent.Exceptions;
using Microsoft.ServiceProfiler.HttpPipeline;
using IStampFrontendClient = Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.IStampFrontendClient;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Frontend;

/// <summary>
/// Base class for both Service Profiler and Snapshot Debugger clients to connect to the frontend server.
/// </summary>
public abstract class StampFrontendClient : IStampFrontendClient
{
    public StampFrontendClient(
        Uri host,
        Guid instrumentationKey,
        string machineName,
        string featureVersion = null,
        string userAgent = null,
        TokenCredential tokenCredential = null,
        bool skipCertificateValidation = false,
        bool enableAzureCoreDiagnostics = false)
    {
        InstrumentationKey = instrumentationKey;
        MachineName = machineName;
        FeatureVersion = featureVersion;
        Pipeline = BuildHttpPipeline(host, userAgent, tokenCredential, skipCertificateValidation, enableAzureCoreDiagnostics);
    }

    public string FeatureVersion { get; }

    public bool FeatureEnabled
    {
        get
        {
            return _featureEnabled ?? throw new FeatureUnavailableException("Please call " + nameof(GetStampIdAsync) + " first.");
        }

        protected set
        {
            _featureEnabled = value;
        }
    }

    public string StampID
    {
        get
        {
            Debug.Assert(!string.IsNullOrEmpty(_stampID), "You must first call " + nameof(GetStampIdAsync) + " successfully.");
            return _stampID;
        }

        protected set
        {
            _stampID = value;
        }
    }

    /// <inheritdoc/>
    public abstract Task<string> GetStampIdAsync(CancellationToken cancellationToken);

    public Guid InstrumentationKey { get; }

    #region Protected
    protected HttpPipeline Pipeline { get; }

    protected abstract Uri GetStampIDPath();

    protected string MachineName { get; }

    /// <summary>
    /// Validate <paramref name="instrumentationKey"/> is well formed and not empty.
    /// </summary>
    /// <param name="instrumentationKey">The instrumentation key to check.</param>
    /// <exception cref="InstrumentationKeyInvalidException">The <paramref name="instrumentationKey"/> is <see cref="Guid.Empty"/></exception>
    protected static void ThrowIfInstrumentationKeyIsEmpty(Guid instrumentationKey)
    {
        if (instrumentationKey == Guid.Empty)
        {
            throw new InstrumentationKeyInvalidException("Instrumentation Key is empty.");
        }
    }

    #endregion

    #region Private
    private HttpPipeline BuildHttpPipeline(
        Uri baseEndpoint,
        string userAgent,
        TokenCredential tokenCredential,
        bool skipCertificateValidation,
        bool enableAzureCoreDiagnostics)
    {
        var clientOptions = new FrontendClientOptions(baseEndpoint, userAgent, tokenCredential, skipCertificateValidation);

        var perCallPolicies = new HttpPipelinePolicy[]
        {
                new ForbiddenCallbackPolicy(() => _featureEnabled = false),
                new TooManyRequestsThrowsRateLimitExceptionPolicy()
        };

        var perRetryPolicies = new HttpPipelinePolicy[]
        {
                new PreconditionFailedCallbackPolicy(async () => await GetStampIdAsync(default).ConfigureAwait(false))
        };

        if (enableAzureCoreDiagnostics)
        {
            clientOptions.EnableDiagnosticsLogging();
        }

        return HttpPipelineBuilder.Build(
            clientOptions,
            perCallPolicies,
            perRetryPolicies,
            new HttpPipelineTransportOptions { IsClientRedirectEnabled = true },
            new FrontendResponseClassifier());
    }

    private string _stampID;
    private bool? _featureEnabled;
    #endregion
}

