using Azure.Core;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Profiler.Core.Utilities;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.Auth;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Auth;
using Microsoft.Extensions.Logging;
using ServiceProfiler.Common.Utilities;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Core;

internal class AgentStatusSender : IAgentStatusSender
{
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly IAuthTokenProvider _authTokenProvider;
    private readonly ILoggerFactory _loggerFactory;

    private static readonly string CategoryName = typeof(AgentStatusSender).FullName ?? "AgentStatusSender";

    public AgentStatusSender(
        IServiceProfilerContext serviceProfilerContext,
        IAuthTokenProvider authTokenProvider,
        ILoggerFactory loggerFactory)
    {
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _authTokenProvider = authTokenProvider ?? throw new ArgumentNullException(nameof(authTokenProvider));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public async Task SendAsync(ProfilerAgentStatus agentStatus, string reason, CancellationToken cancellationToken)
    {
        AccessToken? accessToken = _authTokenProvider.IsAADAuthenticateEnabled ?
            await _authTokenProvider.GetTokenAsync(cancellationToken).ConfigureAwait(false)
            : null;

        using TelemetryConfiguration telemetryConfiguration = BuildTelemetryConfiguration(accessToken);
        TelemetryClient telemetryClient = new(telemetryConfiguration);

        TraceTelemetry traceTelemetry = new(message: string.Format(ProfilerAgentStatus.TraceTelemetryFormat, ProfilerAgentStatus.EventName, agentStatus.Status, agentStatus.RoleInstance, reason));
        traceTelemetry.Properties["CategoryName"] = CategoryName;
        traceTelemetry.Properties["eventName"] = ProfilerAgentStatus.EventName;
        traceTelemetry.Properties["status"] = agentStatus.Status.ToString();
        traceTelemetry.Properties["instance"] = agentStatus.RoleInstance ?? "Unknown";
        traceTelemetry.Properties["reason"] = reason;

        telemetryClient.TrackTrace(traceTelemetry);
        telemetryClient.Flush();
    }

    private TelemetryConfiguration BuildTelemetryConfiguration(AccessToken? accessToken)
    {
        ConnectionString connectionString = _serviceProfilerContext.ConnectionString ?? throw new InvalidOperationException("Connection string is required.");

        TelemetryConfigurationBuilder telemetryConfigurationBuilder = new(
            connectionString,
            accessToken is null ? null : new StaticAccessTokenCredential(accessToken.Value),
            telemetryInitializers: [new PreventSamplingTelemetryInitializer()],
            _loggerFactory.CreateLogger<TelemetryConfigurationBuilder>()
            );

        TelemetryConfiguration telemetryConfiguration = telemetryConfigurationBuilder.Build();

        return telemetryConfiguration;
    }
}