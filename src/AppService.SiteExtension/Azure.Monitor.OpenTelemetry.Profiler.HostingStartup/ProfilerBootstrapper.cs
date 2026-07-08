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
/// the matching profiler without any code change in the target application.
///
/// Activation is fail-safe: every failure - during detection, during the deferred
/// <see cref="IWebHostBuilder.ConfigureServices"/> callback that registers the profiler (e.g. a
/// <c>MissingMethodException</c>/<c>FileNotFoundException</c> from an incompatible dependency version the
/// application brought), or later - is caught and logged so the host always starts. The profiler simply
/// disables itself; it never takes down the application.
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
    /// Applies the routing decision. A supported OpenTelemetry-based stack enables the Azure Monitor
    /// OpenTelemetry profiler; the legacy classic Application Insights SDK (2.x) enables the classic
    /// Application Insights profiler; anything else enables nothing.
    ///
    /// The profiler-registration work runs inside a deferred <c>ConfigureServices</c> callback (executed
    /// later, during the application's host build). That callback body is wrapped in try/catch here: if the
    /// profiler cannot be registered - most importantly when the application brought an incompatible version
    /// of a shared dependency (OpenTelemetry, Microsoft.Extensions.*, Azure.Core) that our code was compiled
    /// against - the failure is logged and the application starts WITHOUT the profiler instead of crashing.
    /// </summary>
    internal static void Apply(IWebHostBuilder builder, TelemetryStack stack)
    {
        switch (stack)
        {
            case TelemetryStack.OpenTelemetry:
                BootstrapLog.Info("Detected a supported OpenTelemetry-based telemetry stack. Enabling the Azure Monitor OpenTelemetry profiler.");
                builder.ConfigureServices(services =>
                {
                    try
                    {
                        services.AddAzureMonitorProfiler();
                    }
                    catch (Exception ex)
                    {
                        BootstrapLog.Error("Failed to enable the Azure Monitor OpenTelemetry profiler; the application will start without it.", ex);
                    }
                });
                break;

            case TelemetryStack.LegacyApplicationInsights:
                BootstrapLog.Info("Detected the classic Application Insights SDK (2.x). Enabling the classic Application Insights profiler.");
                builder.ConfigureServices(services =>
                {
                    try
                    {
                        // Mirror the classic HostingStartup (HostingStartup30): ensure the Application
                        // Insights telemetry pipeline the profiler depends on is present, then add the
                        // profiler. Both calls are idempotent.
                        services.AddApplicationInsightsTelemetry();
                        services.AddServiceProfiler();
                    }
                    catch (Exception ex)
                    {
                        BootstrapLog.Error("Failed to enable the classic Application Insights profiler; the application will start without it.", ex);
                    }
                });
                break;

            default:
                BootstrapLog.Error("No supported telemetry stack (OpenTelemetry, the Azure Monitor OpenTelemetry distro, the OpenTelemetry-based Application Insights SDK 3.x, or the classic Application Insights SDK 2.x) was detected. The profiler will NOT be activated.");
                break;
        }
    }
}
