using Azure.Monitor.OpenTelemetry.Profiler;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------------------
// Application Insights ASP.NET Core 3.x + Azure Monitor Profiler
// ------------------------------------------------------------------
// AI SDK 3.x is an OpenTelemetry-based wrapper, so the profiler
// can be enabled without adding Azure.Monitor.OpenTelemetry.AspNetCore.
//
// Choose ONE of the patterns below based on your profiler package version.

// --- Option 1: Azure.Monitor.OpenTelemetry.Profiler 1.0.0-beta10 and later ---
// A single fluent chain:
// builder.Services.AddApplicationInsightsTelemetry().AddAzureMonitorProfiler();

// --- Option 2: Azure.Monitor.OpenTelemetry.Profiler 1.0.0-beta9 and earlier ---
// Separate calls:
builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddOpenTelemetry().AddAzureMonitorProfiler();

var app = builder.Build();

app.MapGet("/", async () =>
{
    await Task.Delay(2000); // 2 seconds delay
    return "Hello World!";
});

app.Run();
