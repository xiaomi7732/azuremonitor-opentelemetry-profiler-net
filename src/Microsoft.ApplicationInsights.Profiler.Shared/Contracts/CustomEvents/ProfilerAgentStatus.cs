using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;

internal record ProfilerAgentStatus
{
    public const string TraceTelemetryFormat = "CustomEvent: {eventName} | {status} {instance} {reason}";

    public const string EventName = "ProfilerStatus";

    public static readonly ProfilerAgentStatus Default = new();

    public DateTime Timestamp { get; init; }
    public AgentStatus Status { get; init; }
    public string RoleName { get; init; } = null!;
    public string RoleInstance { get; set; } = null!;
}
