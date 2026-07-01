// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Reflection;

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

/// <summary>
/// Detects the telemetry stack by inspecting the host application's own <c>*.deps.json</c> file.
/// This reflects the packages the developer chose to reference and is unaffected by the profiler
/// assemblies we bundle alongside it (those live in a separate folder), so it cleanly distinguishes the
/// app's telemetry choice from our payload.
/// </summary>
/// <remarks>
/// The detection deliberately performs a lightweight text scan instead of parsing with
/// <c>System.Text.Json</c>. This code runs extremely early (while the host is being built) and must not
/// force-load a specific <c>System.Text.Json</c> version - the bundled profiler stack may reference a
/// newer version than the target application, which would fail to bind at this point.
/// </remarks>
internal sealed class DepsFileTelemetryStackDetector : ITelemetryStackDetector
{
    private readonly Func<string?> _depsFilePathProvider;
    private readonly Func<string, string?> _readAllText;

    public DepsFileTelemetryStackDetector()
        : this(GetEntryAssemblyDepsPath, TryReadAllText)
    {
    }

    internal DepsFileTelemetryStackDetector(Func<string?> depsFilePathProvider, Func<string, string?> readAllText)
    {
        _depsFilePathProvider = depsFilePathProvider ?? throw new ArgumentNullException(nameof(depsFilePathProvider));
        _readAllText = readAllText ?? throw new ArgumentNullException(nameof(readAllText));
    }

    public TelemetryStack Detect()
    {
        string? path = _depsFilePathProvider();
        if (string.IsNullOrEmpty(path))
        {
            BootstrapLog.Info("Could not locate the application's .deps.json; treating telemetry stack as undetected.");
            return TelemetryStack.None;
        }

        string? content = _readAllText(path!);
        if (string.IsNullOrEmpty(content))
        {
            BootstrapLog.Info($"The application's .deps.json at '{path}' was empty or unreadable; treating telemetry stack as undetected.");
            return TelemetryStack.None;
        }

        return DetectFromDepsJson(content!);
    }

    /// <summary>
    /// Classifies the telemetry stack from the package names referenced in a <c>.deps.json</c> document
    /// using a dependency-free text scan.
    /// </summary>
    /// <remarks>
    /// Detection is by the top-level integration package the developer added, with precedence, because
    /// the classic Application Insights ASP.NET Core SDK (2.22+) transitively pulls in the OpenTelemetry
    /// SDK (via <c>Azure.Monitor.OpenTelemetry.Exporter</c>). "OpenTelemetry is present" alone therefore
    /// does not imply the app uses OpenTelemetry for its telemetry.
    /// </remarks>
    internal static TelemetryStack DetectFromDepsJson(string depsJson)
    {
        // 1. Explicit Azure Monitor OpenTelemetry distro. It does not reference the classic ASP.NET Core
        //    SDK, so its presence is an unambiguous OpenTelemetry signal.
        if (ContainsPackageToken(depsJson, "Azure.Monitor.OpenTelemetry.AspNetCore"))
        {
            return TelemetryStack.OpenTelemetry;
        }

        // 2. Classic Application Insights SDK integration package (ASP.NET Core or Worker Service). Takes
        //    precedence over the generic OpenTelemetry check below, which it pulls in transitively.
        if (ContainsPackageToken(depsJson, "Microsoft.ApplicationInsights.AspNetCore")
            || ContainsPackageToken(depsJson, "Microsoft.ApplicationInsights.WorkerService"))
        {
            return TelemetryStack.ApplicationInsights;
        }

        // 3. Manual OpenTelemetry setup (SDK / hosting) without either distro or the classic SDK.
        if (ContainsPackageToken(depsJson, "OpenTelemetry"))
        {
            return TelemetryStack.OpenTelemetry;
        }

        return TelemetryStack.None;
    }

    /// <summary>
    /// Returns true when the document contains a quoted JSON token that begins with the given package
    /// name (a library key such as <c>"OpenTelemetry/1.9.0"</c> or a dependency reference such as
    /// <c>"OpenTelemetry.Api"</c>).
    /// </summary>
    private static bool ContainsPackageToken(string depsJson, string package) =>
        depsJson.IndexOf("\"" + package, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string? GetEntryAssemblyDepsPath()
    {
        Assembly? entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly is null)
        {
            return null;
        }

        string? directory = null;
        string? location = entryAssembly.Location;
        if (!string.IsNullOrEmpty(location))
        {
            directory = Path.GetDirectoryName(location);
        }

        if (string.IsNullOrEmpty(directory))
        {
            directory = AppContext.BaseDirectory;
        }

        string? assemblyName = entryAssembly.GetName().Name;
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(assemblyName))
        {
            return null;
        }

        string candidate = Path.Combine(directory!, assemblyName + ".deps.json");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? TryReadAllText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
