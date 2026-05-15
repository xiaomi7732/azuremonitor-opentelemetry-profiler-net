# ⚠️ Internal Environment Variable — Testing Only

> **This environment variable is for internal testing only and will be removed in a future release.**
> Do not depend on it in production.

## `MICROSOFT_PROFILER_INTERNAL_REQUEST_SOURCE`

Controls which EventSource the profiler uses to detect ASP.NET Core HTTP request start/stop events. It exists as a kill switch during the migration from `OpenTelemetry-Sdk` EventSource to `DiagnosticSource`.

### Background

The OpenTelemetry SDK EventSource is internal and not reliable to depend on — changes in the SDK can break the profiler without warning. We are migrating to `Microsoft-Diagnostics-DiagnosticSource` as the primary request event source. This environment variable allows switching between sources for testing and validation.

### Accepted Values

| Value | Description |
|-------|-------------|
| `ds` | **(Default)** Use `Microsoft-Diagnostics-DiagnosticSource` only |
| `otel` | Use `OpenTelemetry-Sdk` RequestStart/Stop events only (legacy) |
| `both` | Subscribe to both sources; deduplication ensures exactly one Start/Stop pair per request |

### Usage

```bash
# Linux / macOS
export MICROSOFT_PROFILER_INTERNAL_REQUEST_SOURCE=both

# Windows (PowerShell)
$env:MICROSOFT_PROFILER_INTERNAL_REQUEST_SOURCE = "both"

# Windows (cmd)
set MICROSOFT_PROFILER_INTERNAL_REQUEST_SOURCE=both
```

### Behavior

- If the variable is **unset or empty**, the default mode (`ds`) is used.
- If the variable contains an **unrecognized value**, it falls back to the default (`ds`) and logs a warning.
- Values are **case-insensitive**.
