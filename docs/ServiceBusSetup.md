# Profiling Azure Service Bus Applications

This guide explains how to enable the Azure Monitor OpenTelemetry Profiler for applications that process messages from Azure Service Bus using `ServiceBusProcessor` or `ServiceBusSessionProcessor`.

## Prerequisites

- Complete the [Get Started](../Readme.md#get-started) steps first (install packages, call `AddAzureMonitorProfiler()`).
- Your application uses [`Azure.Messaging.ServiceBus`](https://www.nuget.org/packages/Azure.Messaging.ServiceBus/) to process messages.

## Enable the Azure SDK ActivitySource

The Azure Service Bus SDK's `ActivitySource` is currently marked as experimental. By default, the SDK uses the legacy `DiagnosticListener` path, which the profiler cannot observe. You must opt in to the `ActivitySource`-based instrumentation so the profiler can capture Service Bus request activities.

### Option A: In code (recommended)

Add this line **before** any Azure SDK client is created — typically at the top of `Program.cs`:

```csharp
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);
```

**Full example:**

```csharp
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Monitor.OpenTelemetry.Profiler;

// Enable ActivitySource in Azure SDK so the profiler can capture Service Bus activities.
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOpenTelemetry()
    .UseAzureMonitor()
    .AddAzureMonitorProfiler();

builder.Services.AddSingleton(new ServiceBusClient(
    "your-namespace.servicebus.windows.net",
    new DefaultAzureCredential()));

// Register your hosted service that processes messages...
builder.Services.AddHostedService<QueueProcessorWorker>();

var host = builder.Build();
host.Run();
```

### Option B: Via runtime configuration (no code change)

Add a `runtimeconfig.template.json` file to your project root (next to `.csproj`):

```json
{
  "configProperties": {
    "Azure.Experimental.EnableActivitySource": true
  }
}
```

The MSBuild system merges this into the output `<YourApp>.runtimeconfig.json` at build time. The setting takes effect when the application starts.

> **Note:** This option requires a rebuild after adding the file. If the file is not present at build time, the switch will not be included in the output.

## How it works

When the `ActivitySource` is enabled, the Service Bus SDK emits `System.Diagnostics.Activity` instances for each processed message. The profiler listens for these activities and correlates performance traces (CPU samples, call stacks) with individual message-processing operations — just as it does for incoming HTTP requests in ASP.NET Core.

In Application Insights, you will see profiler traces attached to Service Bus `process` operations, allowing you to inspect the hot path and identify performance bottlenecks in your message handlers.

## Troubleshooting

If you encounter issues with profiling Service Bus applications, please [open an issue](https://github.com/Azure/azuremonitor-opentelemetry-profiler-net/issues/new) with your environment details and relevant log output.
