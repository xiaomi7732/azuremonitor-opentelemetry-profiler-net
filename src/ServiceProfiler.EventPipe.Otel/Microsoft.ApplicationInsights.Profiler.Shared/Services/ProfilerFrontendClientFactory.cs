// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Azure.Core;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.Auth;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Agent.FrontendClient;
using Microsoft.ServiceProfiler.Utilities;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal class ProfilerFrontendClientFactory : IProfilerFrontendClientFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly UserConfigurationBase _userConfiguration;

    public ProfilerFrontendClientFactory(
        IServiceProvider serviceProvider,
        IServiceProfilerContext serviceProfilerContext,
        IOptions<UserConfigurationBase> userConfiguration)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _userConfiguration = userConfiguration?.Value ?? throw new ArgumentNullException(nameof(userConfiguration));
    }

    public IProfilerFrontendClient CreateProfilerFrontendClient()
    {
        IAuthTokenProvider authTokenProvider = _serviceProvider.GetRequiredService<IAuthTokenProvider>();

        TokenCredential? credential = authTokenProvider.IsAADAuthenticateEnabled ?
            ActivatorUtilities.CreateInstance<AADAuthTokenCredential>(_serviceProvider) :
            null;

        return ActivatorUtilities.CreateInstance<ProfilerFrontendClient>(
            _serviceProvider,
            _serviceProfilerContext.StampFrontendEndpointUrl,
            _serviceProfilerContext.AppInsightsInstrumentationKey,
            _serviceProfilerContext.MachineName,
            "1.0.0",
            FormattableString.Invariant($"ServiceProfilerEventPipeAgent/{EnvironmentUtilities.ExecutingAssemblyInformationalVersion}"),
            credential,
            _userConfiguration.SkipEndpointCertificateValidation);
    }
}
