// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Azure.Core;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Auth;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.Auth;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Frontend;

internal class ProfilerFrontendClientFactory : IProfilerFrontendClientFactory
{
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly ILoggerFactory _loggerFactory;
    private readonly UserConfigurationBase _userConfiguration;
    private readonly IAuthTokenProvider _authTokenProvider;

    public ProfilerFrontendClientFactory(
        IAuthTokenProvider authTokenServiceFactory,
        IServiceProfilerContext serviceProfilerContext,
        IOptions<UserConfigurationBase> userConfiguration,
        ILoggerFactory loggerFactory)
    {
        _serviceProfilerContext = serviceProfilerContext ?? throw new System.ArgumentNullException(nameof(serviceProfilerContext));
        _loggerFactory = loggerFactory ?? throw new System.ArgumentNullException(nameof(loggerFactory));
        _userConfiguration = userConfiguration?.Value ?? throw new System.ArgumentNullException(nameof(userConfiguration));
        _authTokenProvider = authTokenServiceFactory ?? throw new System.ArgumentNullException(nameof(authTokenServiceFactory));
    }
    public IProfilerFrontendClient CreateProfilerFrontendClient()
    {

        TokenCredential credential = _authTokenProvider.IsAADAuthenticateEnabled ?
            new AADAuthTokenCredential(
                _authTokenProvider,
                _loggerFactory.CreateLogger<AADAuthTokenCredential>()) :
            null;

        return new ProfilerFrontendClient(
            host: _serviceProfilerContext.StampFrontendEndpointUrl,
            instrumentationKey: _serviceProfilerContext.AppInsightsInstrumentationKey,
            machineName: _serviceProfilerContext.MachineName,
            featureVersion: "1.0.0",
            userAgent: System.FormattableString.Invariant($"ServiceProfilerEventPipeAgent/{EnvironmentUtilities.ExecutingAssemblyInformationalVersion}"),
            tokenCredential: credential,
            skipCertificateValidation: _userConfiguration.SkipEndpointCertificateValidation);
    }
}
