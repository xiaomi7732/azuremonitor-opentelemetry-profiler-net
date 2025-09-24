using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;

internal record ProfilerAgentStatus
{
    // Keep these 2 formats in sync. 
    // Do not change the existing format as it is used in production and changing it will break existing queries.
    public const string TraceTelemetryFormat = "CustomEvent: {EventName} | {Status} {Instance} {Reason}";
    public const string TraceTelemetryFormatWithIndexHolder = "CustomEvent: {0} | {1} {2} {3}";

    public const string EventName = "ProfilerStatus";

    public static readonly ProfilerAgentStatus Default = new();

    public DateTime Timestamp { get; init; }
    public AgentStatus Status { get; init; }
    public string RoleName { get; init; } = null!;
    public string RoleInstance { get; set; } = null!;
}
