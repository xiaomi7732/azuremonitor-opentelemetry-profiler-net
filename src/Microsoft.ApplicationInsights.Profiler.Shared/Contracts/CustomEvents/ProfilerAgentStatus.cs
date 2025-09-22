using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;

internal record ProfilerAgentStatus
{
    // Keep these 2 formats in sync.
    public const string TraceTelemetryFormat = "CustomEvent: {eventName} | {status} {instance} {reason}";
    public const string TraceTelemetryFormatWithIndexHolder = "CustomEvent: {0} | {1} {2} {3}";

    public const string EventName = "ProfilerStatus";

    public static readonly ProfilerAgentStatus Default = new();

    public DateTime Timestamp { get; init; }
    public AgentStatus Status { get; init; }
    public string RoleName { get; init; } = null!;
    public string RoleInstance { get; set; } = null!;
}
