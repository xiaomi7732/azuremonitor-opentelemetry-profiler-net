//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.Orchestrations;

// TODO: Make this a hosted service running on the background.
internal sealed class RemoteSettingsService : RemoteSettingsServiceBase
{
    public RemoteSettingsService(
        IProfilerFrontendClientFactory frontendClientFactory,
        IOptions<UserConfigurationBase> userConfigurationOptions,
        ILogger<RemoteSettingsService> logger) : base(frontendClientFactory, userConfigurationOptions, logger)
    {
    }

    protected override IDisposable EnterInternalZone()
    {
        return SuppressInstrumentationScope.Begin();
    }
}
