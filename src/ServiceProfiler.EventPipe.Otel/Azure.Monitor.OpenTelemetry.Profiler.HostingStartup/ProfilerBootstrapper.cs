// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Monitor.OpenTelemetry.Profiler;
using Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

[assembly: HostingStartup(typeof(ProfilerBootstrapper))]

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

/// <summary>
/// Codeless entry point activated by the ASP.NET Core runtime when this assembly is listed in
/// <c>ASPNETCORE_HOSTINGSTARTUPASSEMBLIES</c>. It detects the application's telemetry stack and enables
/// the matching profiler without any code change in the target application. Any failure is swallowed so
/// the host always starts.
/// </summary>
public sealed class ProfilerBootstrapper : IHostingStartup
{
    private readonly ITelemetryStackDetector _detector;

    public ProfilerBootstrapper()
        : this(new DepsFileTelemetryStackDetector())
    {
    }

    internal ProfilerBootstrapper(ITelemetryStackDetector detector)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
    }

    /// <inheritdoc />
    public void Configure(IWebHostBuilder builder)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        try
        {
            TelemetryStack stack = _detector.Detect();
            Apply(builder, stack);
        }
        catch (Exception ex)
        {
            BootstrapLog.Error("Codeless profiler bootstrap failed; the application will start without the profiler.", ex);
        }
    }

    /// <summary>
    /// Applies the routing decision. Exactly one detected stack enables the matching profiler; both or
    /// neither logs an error and enables nothing (per the v1 routing rule).
    /// </summary>
    internal static void Apply(IWebHostBuilder builder, TelemetryStack stack)
    {
        switch (stack)
        {
            case TelemetryStack.OpenTelemetry:
                BootstrapLog.Info("Detected OpenTelemetry. Enabling the Azure Monitor OpenTelemetry profiler.");
                builder.ConfigureServices(services => services.AddAzureMonitorProfiler());
                break;

            case TelemetryStack.ApplicationInsights:
                BootstrapLog.Info("Detected the Application Insights SDK. Enabling the classic Application Insights profiler.");
                builder.ConfigureServices(services =>
                {
                    // Mirror the classic HostingStartup (HostingStartup30): ensure the Application Insights
                    // telemetry pipeline the profiler depends on is present, then add the profiler. Both
                    // calls are idempotent.
                    services.AddApplicationInsightsTelemetry();
                    services.AddServiceProfiler();
                });
                break;

            case TelemetryStack.Both:
                BootstrapLog.Error("Both the Application Insights SDK and OpenTelemetry were detected; cannot determine which profiler to enable. The profiler will NOT be activated.");
                break;

            default:
                BootstrapLog.Error("No supported telemetry stack (Application Insights SDK or OpenTelemetry) was detected. The profiler will NOT be activated.");
                break;
        }
    }
}
