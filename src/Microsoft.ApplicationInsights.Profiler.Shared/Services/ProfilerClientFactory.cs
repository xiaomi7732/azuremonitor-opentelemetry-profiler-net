// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Azure.Core;
using Azure.Monitor.Diagnostics.Profiler;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.Auth;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Utilities;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

/// <summary>
/// Creates a <see cref="ProfilerClient"/> instance from the current service context.
/// Please do NOT inject this directly. Instead, inject <see cref="IProfilerClient"/>.
/// </summary>
internal class ProfilerClientFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly UserConfigurationBase _userConfiguration;
    private readonly ILogger _logger;

    public ProfilerClientFactory(
        IServiceProvider serviceProvider,
        IServiceProfilerContext serviceProfilerContext,
        IOptions<UserConfigurationBase> userConfiguration,
        ILogger<ProfilerClientFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _userConfiguration = userConfiguration?.Value ?? throw new ArgumentNullException(nameof(userConfiguration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IProfilerClient CreateProfilerClient()
    {
#pragma warning disable CS0618 // Type or member is obsolete
        if (_userConfiguration.SkipEndpointCertificateValidation)
        {
            _logger.LogWarning("SkipEndpointCertificateValidation is deprecated and no longer supported. The setting will be ignored.");
        }
#pragma warning restore CS0618

        IAuthTokenProvider authTokenProvider = _serviceProvider.GetRequiredService<IAuthTokenProvider>();

        TokenCredential? credential = authTokenProvider.IsAADAuthenticateEnabled ?
            ActivatorUtilities.CreateInstance<AADAuthTokenCredential>(_serviceProvider) :
            null;

        ProfilerClientOptions options = new()
        {
            Endpoint = _serviceProfilerContext.StampFrontendEndpointUrl,
            InstrumentationKey = _serviceProfilerContext.AppInsightsInstrumentationKey.ToString("D"),
            MachineName = _serviceProfilerContext.MachineName,
            UserAgent = FormattableString.Invariant($"ServiceProfilerEventPipeAgent-OTel/{EnvironmentUtilities.ExecutingAssemblyInformationalVersion}"),
        };

        return new ProfilerClient(options, credential);
    }
}
