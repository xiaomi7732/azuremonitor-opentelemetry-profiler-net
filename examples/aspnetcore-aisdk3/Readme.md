# Enable Azure Monitor Profiler with Application Insights ASP.NET Core 3.x

> ⚠️ **Experimental** — This integration is under active development. Please [report any issues](https://github.com/Azure/azuremonitor-opentelemetry-profiler-net/issues/new) you encounter.

This example shows how to enable Azure Monitor Profiler in an ASP.NET Core application that uses [`Microsoft.ApplicationInsights.AspNetCore`](https://www.nuget.org/packages/Microsoft.ApplicationInsights.AspNetCore/) **3.x** (the OpenTelemetry-based wrapper).

> **Using Azure Monitor OpenTelemetry distro instead?** See the [aspnetcore-webapi](../aspnetcore-webapi/) example.

## Create an application

Run this command to create a web api application:

```shell
dotnet new web -n AISDK3App -o . -f net8.0
```

## Add NuGet packages

Add the following 2 packages — see [AISDK3App.csproj](./AISDK3App.csproj) for details:

```xml
<ItemGroup>
    <PackageReference Include="Microsoft.ApplicationInsights.AspNetCore" Version="[3.*-*, 4.0.0)" />
    <PackageReference Include="Azure.Monitor.OpenTelemetry.Profiler" Version="[1.*-*, 2.0.0)" />
</ItemGroup>
```

Restore the packages:

```shell
dotnet restore AISDK3App.csproj
```

## Update the code to enable the profiler

Application Insights ASP.NET Core 3.x already configures OpenTelemetry internally, so you do **not** need `Azure.Monitor.OpenTelemetry.AspNetCore` or `UseAzureMonitor()`. Just call `AddApplicationInsightsTelemetry()` and then enable the profiler.

The API differs slightly depending on which version of `Azure.Monitor.OpenTelemetry.Profiler` you are using:

### Azure.Monitor.OpenTelemetry.Profiler **1.0.0-beta10 and later**

A single fluent chain:

```csharp
using Azure.Monitor.OpenTelemetry.Profiler;

builder.Services.AddApplicationInsightsTelemetry().AddAzureMonitorProfiler();
```

### Azure.Monitor.OpenTelemetry.Profiler **1.0.0-beta9 and earlier**

Separate calls:

```csharp
using Azure.Monitor.OpenTelemetry.Profiler;

builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddOpenTelemetry().AddAzureMonitorProfiler();
```

See [Program.cs](./Program.cs) for the full example.

## Set up the connection string

Refer to the [connection string section](https://learn.microsoft.com/azure/azure-monitor/app/opentelemetry-enable?tabs=aspnetcore#paste-the-connection-string-in-your-environment) for all options.

Quick way for local testing using an environment variable in PowerShell:

```powershell
$env:APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=5d..."
```

_Tip: Copy the connection string from the **Overview** blade of your Application Insights resource._

## Add a delay to your endpoint (for testing)

```csharp
app.MapGet("/", async () =>
{
    await Task.Delay(2000); // 2 seconds delay
    return "Hello World!";
});
```

⚠️ Trace analysis might fail if the operation completes too quickly (e.g., within 10 milliseconds).

## Run your application

```shell
dotnet run
```

Generate some traffic for the profiler to capture:

```shell
curl http://localhost:5082
```

After a few minutes, profiler traces will appear in Application Insights. Follow [these instructions](https://learn.microsoft.com/azure/azure-monitor/profiler/profiler-data) to view them.
