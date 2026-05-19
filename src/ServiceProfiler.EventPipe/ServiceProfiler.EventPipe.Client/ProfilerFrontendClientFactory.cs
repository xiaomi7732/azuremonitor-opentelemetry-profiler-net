// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Azure.Core;
using Azure.Monitor.Diagnostics.Profiler;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.Auth;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Utilities;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Core;

/// <summary>
/// Creates a <see cref="ProfilerClient"/> instance from the current service context.
/// Please do NOT inject this directly. Instead, inject <see cref="IProfilerClient"/>.
/// </summary>
internal class ProfilerClientFactory
{
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly ILoggerFactory _loggerFactory;
    private readonly UserConfiguration _userConfiguration;
    private readonly IAuthTokenProvider _authTokenProvider;

    public ProfilerClientFactory(
        IAuthTokenProvider authTokenServiceFactory,
        IServiceProfilerContext serviceProfilerContext,
        IOptions<UserConfiguration> userConfiguration,
        ILoggerFactory loggerFactory)
    {
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _userConfiguration = userConfiguration?.Value ?? throw new ArgumentNullException(nameof(userConfiguration));
        _authTokenProvider = authTokenServiceFactory ?? throw new ArgumentNullException(nameof(authTokenServiceFactory));
    }

    public IProfilerClient CreateProfilerClient()
    {
        TokenCredential? credential = _authTokenProvider.IsAADAuthenticateEnabled ?
            new AADAuthTokenCredential(
                _authTokenProvider,
                _loggerFactory.CreateLogger<AADAuthTokenCredential>()) :
            null;

        ProfilerClientOptions options = new()
        {
            Endpoint = _serviceProfilerContext.StampFrontendEndpointUrl,
            InstrumentationKey = _serviceProfilerContext.AppInsightsInstrumentationKey.ToString("D"),
            MachineName = _serviceProfilerContext.MachineName,
            UserAgent = FormattableString.Invariant($"ServiceProfilerEventPipeAgent/{EnvironmentUtilities.ExecutingAssemblyInformationalVersion}"),
        };

        return new ProfilerClient(options, credential);
    }
}
