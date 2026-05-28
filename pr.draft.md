# PR Title

**Add Azure Service Bus processor activity support and fix EventSource ActivityTracker nesting**

# PR Description

## Summary

Adds support for capturing Azure Service Bus processor message activities alongside ASP.NET Core HTTP request activities. Also fixes an EventSource ActivityTracker nesting issue that affected relay event ActivityId paths across all profiler variants.

## Motivation

The profiler previously only captured ASP.NET Core HTTP-in request activities. Applications using Azure Service Bus processors had no visibility into message processing performance. This PR extends the profiler to instrument Service Bus processing events, enabling profiling traces for message-driven workloads.

## Changes

### Service Bus support (OTel profiler)

- **`ServiceBusActivityIdResetListener`** — New `ActivityListener` that resets the thread-local EventSource `ActivityId` when Service Bus processor activities start, preventing the Service Bus SDK's internal activity from contaminating the relay event's `ActivityId` path
- **`DiagnosticSourceEventSourceHandler`** — Extended to recognize Service Bus processor activity names
- **`RequestActivityRelay`** — Generalized to handle both HTTP and Service Bus request activities, with deduplication across event source handlers
- **`TraceSessionListenerFactory`** — Registers the new `ServiceBusActivityIdResetListener` via DI
- **`DiagnosticsClientTraceConfiguration`** — Added `Azure.Messaging.ServiceBus` EventSource to the EventPipe provider list

### EventSource ActivityTracker fix (all profilers)

- **`AzureMonitorOpenTelemetryProfilerDataAdapterEventSource`** (OTel) — Added `Task = Tasks.Request` and `ActivityOptions = EventActivityOptions.Disable` on `RequestStart`/`RequestStop` events, preventing EventSource's `ActivityTracker` from pushing/popping the thread's `ActivityId` during `WriteEvent`
- **`ApplicationInsightsDataRelayEventSource`** (Classic) — Changed `ActivityOptions` from `None` to `Disable`, matching the v3.0 EventSource pattern
- **`TraceSessionListener` / `TraceSessionListener30`** (Classic) — Save and restore `CurrentThreadActivityId` around relay calls, matching the pattern used by the OTel `RequestActivityRelay`
- **`SampleActivity.IsValid`** (Shared) — Updated comment to reflect the new invariant; kept `StartsWith` for backward compatibility

## Files Changed

| File | Change |
|------|--------|
| `ServiceBusActivityIdResetListener.cs` | **New** — resets EventSource ActivityId for Service Bus activities |
| `DiagnosticSourceEventSourceHandler.cs` | Recognize Service Bus activity names |
| `RequestActivityRelay.cs` | Handle HTTP + Service Bus, deduplicate across handlers |
| `TraceSessionListenerFactory.cs` (OTel) | Register `ServiceBusActivityIdResetListener` |
| `DiagnosticsClientTraceConfiguration.cs` | Add Service Bus EventSource provider |
| `AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.cs` | Add `Tasks`, use `Disable` + `Start/Stop` opcodes |
| `ApplicationInsightsDataRelayEventSource.cs` (Classic) | `ActivityOptions.None` → `Disable` |
| `TraceSessionListener.cs` (Classic) | Save/restore `CurrentThreadActivityId` |
| `TraceSessionListener30.cs` (Classic) | Save/restore `CurrentThreadActivityId` |
| `SampleActivity.cs` (Shared) | Updated validation comment |

## Testing

- OTel private package builds successfully
- Code reviewed with GPT 5.5 and Claude Opus 4.7 (2 consecutive clean iterations per round)

## Risk

- **Low** — The `EventActivityOptions.Disable` pattern is proven in `ApplicationInsightsDataRelayEventSource30` (used since .NET Core 3.0). The save/restore of `CurrentThreadActivityId` matches the OTel relay's existing pattern.
- Classic non-30 EventSource change only affects .NET Core 2.x runtime paths (EOL) plus the multi-listener enumeration path.
