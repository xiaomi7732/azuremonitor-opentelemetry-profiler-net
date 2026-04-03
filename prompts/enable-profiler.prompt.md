---
mode: 'agent'
---

# Add Azure Monitor OpenTelemetry Profiler to your ASP.NET Core Application

You are a software engineer tasked with integrating the Azure Monitor OpenTelemetry Profiler into an ASP.NET Core application. The profiler will help in monitoring and diagnosing performance issues in the application.

You are not supposed to change any other parts of the application, only to add the OpenTelemetry Profiler.

Read this instruction carefully and follow the steps to ensure a successful integration.

Ask for the necessary information to complete the integration, such as:
- Which project to integrate the profiler into (if there are multiple projects).

Here are the steps you need to follow:

1. **Identify the Project**: Determine which ASP.NET Core project you need to integrate the OpenTelemetry Profiler into. If there are multiple projects, ask for the specific one.

2. **Detect which Application Insights SDK is in use** by inspecting the project's `.csproj` file and `Program.cs` / `Startup.cs`:
   - If the project references `Azure.Monitor.OpenTelemetry.AspNetCore` or calls `UseAzureMonitor()`, follow **Path A** (Azure Monitor OpenTelemetry distro).
   - If the project references `Microsoft.ApplicationInsights.AspNetCore` version 3.x or later, or calls `AddApplicationInsightsTelemetry()`, follow **Path B** (Application Insights 3.x).
   - If neither SDK is present, ask the user which SDK they want to use.

3. **Path A — Azure Monitor OpenTelemetry Distro**:
   - **Add the latest required NuGet packages**, including the prerelease versions if necessary:
     - Azure.Monitor.OpenTelemetry.AspNetCore
     - Azure.Monitor.OpenTelemetry.Profiler
   - Do not install any other packages or change existing ones.
   - Append the call to `AddAzureMonitorProfiler()` in the code. For example:

     ```csharp
     using Azure.Monitor.OpenTelemetry.AspNetCore;
     // Import the Azure.Monitor.OpenTelemetry.Profiler namespace.
     using Azure.Monitor.OpenTelemetry.Profiler;

     // ...
     builder.Services.AddOpenTelemetry()
           .UseAzureMonitor()
           .AddAzureMonitorProfiler();  // Add Azure Monitor Profiler
     ```

4. **Path B — Application Insights ASP.NET Core 3.x**:
   - **Add the required NuGet package** (the project should already have `Microsoft.ApplicationInsights.AspNetCore` 3.x):
     - Azure.Monitor.OpenTelemetry.Profiler
   - Do not install any other packages or change existing ones. Do **not** add `Azure.Monitor.OpenTelemetry.AspNetCore`.
   - Enable the profiler in the code. For example:

     ```csharp
     using Azure.Monitor.OpenTelemetry.Profiler;

     // ...
     // If using Azure.Monitor.OpenTelemetry.Profiler 1.0.0-beta10 or later:
     builder.Services.AddApplicationInsightsTelemetry().AddAzureMonitorProfiler();

     // If using Azure.Monitor.OpenTelemetry.Profiler 1.0.0-beta9 or earlier:
     // builder.Services.AddApplicationInsightsTelemetry();
     // builder.Services.AddOpenTelemetry().AddAzureMonitorProfiler();
     ```

5. Show user the instructions about how to setup the connection string for Azure Monitor OpenTelemetry distro, so that the profiler can send data to Azure Monitor.
