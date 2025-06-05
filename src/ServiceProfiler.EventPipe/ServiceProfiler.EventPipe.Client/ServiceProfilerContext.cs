//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Agent.Exceptions;
using Microsoft.ServiceProfiler.Contract;
using Microsoft.ServiceProfiler.Utilities;

namespace Microsoft.ApplicationInsights.Profiler.Core
{
    internal sealed class ServiceProfilerContext : IServiceProfilerContext
    {
        public ServiceProfilerContext(
            IOptions<TelemetryConfiguration> telemetryConfiguration,
            IEndpointProvider endpointProvider,
            IOptions<UserConfiguration> userConfiguration,
            AppInsightsProfileFetcher appInsightsProfileFetcher,
            ILogger<IServiceProfilerContext> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (endpointProvider is null)
            {
                throw new ArgumentNullException(nameof(endpointProvider));
            }

            _telemetryConfiguration = telemetryConfiguration.Value ?? throw new ArgumentNullException(nameof(telemetryConfiguration));
            _appInsightsProfileFetcher = appInsightsProfileFetcher ?? throw new ArgumentNullException(nameof(appInsightsProfileFetcher));

            AppInsightsInstrumentationKey = GetAppInsightsInstrumentationKeyInGuid(_telemetryConfiguration.InstrumentationKey);

            // Get appId as early as possible without blocking the initialization.
            var _forget = Task.Run(async () =>
                {
                    AppInsightsAppId = await GetAppInsightsAppIdAsync().ConfigureAwait(false);
                });

            string endpointByConnectionString = endpointProvider.GetEndpoint(EndpointName.ProfilerEndpoint)?.AbsoluteUri;
            string endpointByUserConfiguration = userConfiguration.Value?.Endpoint;
            if (string.IsNullOrEmpty(endpointByConnectionString) && string.IsNullOrEmpty(endpointByUserConfiguration))
            {
                // Either are set, use default;
                StampFrontendEndpointUrl = new Uri(FrontendEndpoints.ProdGlobal, UriKind.Absolute);
            }
            else if (!string.IsNullOrEmpty(endpointByUserConfiguration))
            {
                // Either set by user configuration only or user configuration takes precedence.
                StampFrontendEndpointUrl = new Uri(endpointByUserConfiguration, UriKind.Absolute);
            }
            else
            {
                // When it runs here, it means there's no userconfiguration but there's connection string for the endpoint.
                StampFrontendEndpointUrl = new Uri(endpointByConnectionString, UriKind.Absolute);
            }

            // So that we know what's the final decision for the endpoint:
            _logger.LogInformation("Profiler Endpoint: {endpoint}", StampFrontendEndpointUrl);
        }

        /// <summary>
        /// Gets the machine name of the application running on.
        /// </summary>
        public string MachineName => EnvironmentUtilities.MachineName;

        /// <summary>
        /// Gets the cancellation token that used to terminate service profiler.
        /// </summary>
        /// <remarks>We don't dispose this explicitly because this suppose to be disposed only when the app exits.</remarks>
        public CancellationTokenSource ServiceProfilerCancellationTokenSource { get; } = new CancellationTokenSource();

        /// <summary>
        /// Gets the application insights instrumentation key to use for service profiler data.
        /// </summary>
        public Guid AppInsightsInstrumentationKey { get; }

        /// <summary>
        /// Get the app id after it is fetched. Returns <see cref="Guid.Empty"/> when it is not yet fetched.
        /// </summary>
        public Guid AppInsightsAppId { get; private set; }

        /// <summary>
        /// Get app insights app id.
        /// </summary>
        /// <returns></returns>
        public async Task<Guid> GetAppInsightsAppIdAsync()
        {
            // If it has been fetched, return it immediately.
            Guid result;
            if (AppInsightsAppId != Guid.Empty)
            {
                result = AppInsightsAppId;
            }
            else
            {
                try
                {
                    // Fetch & return.
                    AppInsightsProfile appInsightsProfile = await _appInsightsProfileFetcher.FetchProfileAsync(AppInsightsInstrumentationKey, retryCount: 5).ConfigureAwait(false);
                    result = appInsightsProfile.AppId;
                    OnAppIdFetched(result);
                }
                catch (InstrumentationKeyInvalidException ikie)
                {
                    _logger.LogError(ikie, "Profiler Instrumentation Key is invalid.");
                }
            }

            return result;
        }

        /// <summary>
        /// Invoke the event the iKey is fetched from the server.
        /// </summary>
        public void OnAppIdFetched(Guid appId)
        {
            AppIdFetched?.Invoke(this, new AppIdFetchedEventArgs(appId));
        }

        public bool HasAppInsightsInstrumentationKey => AppInsightsInstrumentationKey != Guid.Empty;

        /// <summary>
        /// Gets the Application Insights Instrumentation Key in form of Guid.
        /// </summary>
        /// <returns>Returns Guid.Empty when the given iKey is null or empty or it can't be parsed.</returns>
        private Guid GetAppInsightsInstrumentationKeyInGuid(string instrumentationIkeyString)
        {
            if (string.IsNullOrEmpty(instrumentationIkeyString))
            {
                return Guid.Empty;
            }

            if (!Guid.TryParse(instrumentationIkeyString, out Guid parsedIKey))
            {
                return Guid.Empty;
            }

            return parsedIKey;
        }

        /// <summary>
        /// Invokes whenever an iKey is fetched from the server.
        /// </summary>
        public event EventHandler<AppIdFetchedEventArgs> AppIdFetched;

        /// <summary>
        /// Gets the Stamp Frontend url.
        /// </summary>
        public Uri StampFrontendEndpointUrl { get; }

        private readonly ILogger _logger;
        private readonly AppInsightsProfileFetcher _appInsightsProfileFetcher;
        private readonly TelemetryConfiguration _telemetryConfiguration;
    }
}
