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
2. **Add the latest required NuGet packages**, including the prerelease versions if necessary:
   - Azure.Monitor.OpenTelemetry.AspNetCore
   - Azure.Monitor.OpenTelemetry.Profiler
   Do not install any other packages or change existing ones.

3. Append the call to AddAzureMonitorProfiler() in the code. For example

  ```csharp
  using Azure.Monitor.OpenTelemetry.AspNetCore;
  // Import the Azure.Monitor.OpenTelemetry.Profiler namespace.
  using Azure.Monitor.OpenTelemetry.Profiler;

  ...
  builder.Services.AddOpenTelemetry()
        .UseAzureMonitor()
        .AddAzureMonitorProfiler();  // Add Azure Monitor Profiler
  ```

4. Show user the instructions about how to setup the connection string for Azure Monitor OpenTelemetry distro, so that the profiler can send data to Azure Monitor.
