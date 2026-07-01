// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Reflection;
using System.Text.Json;

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

/// <summary>
/// Detects the telemetry stack by inspecting the host application's own <c>*.deps.json</c> file.
/// This reflects the packages the developer chose to reference and is unaffected by the profiler
/// assemblies we bundle alongside it (those live in a separate additional-deps file), so it cleanly
/// distinguishes the app's telemetry choice from our payload.
/// </summary>
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
    /// Parses a <c>.deps.json</c> document and classifies the telemetry stack from the referenced
    /// package names under <c>libraries</c>.
    /// </summary>
    internal static TelemetryStack DetectFromDepsJson(string depsJson)
    {
        bool hasOpenTelemetry = false;
        bool hasApplicationInsights = false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(depsJson);
            if (document.RootElement.TryGetProperty("libraries", out JsonElement libraries)
                && libraries.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty library in libraries.EnumerateObject())
                {
                    // Keys look like "OpenTelemetry/1.9.0" or "Microsoft.ApplicationInsights.AspNetCore/2.22.0".
                    string name = library.Name;
                    int separator = name.IndexOf('/');
                    string package = separator >= 0 ? name.Substring(0, separator) : name;

                    if (IsOpenTelemetryPackage(package))
                    {
                        hasOpenTelemetry = true;
                    }
                    else if (IsApplicationInsightsPackage(package))
                    {
                        hasApplicationInsights = true;
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            BootstrapLog.Error("Failed to parse the application's .deps.json; treating telemetry stack as undetected.", ex);
            return TelemetryStack.None;
        }

        return (hasOpenTelemetry, hasApplicationInsights) switch
        {
            (true, false) => TelemetryStack.OpenTelemetry,
            (false, true) => TelemetryStack.ApplicationInsights,
            (true, true) => TelemetryStack.Both,
            _ => TelemetryStack.None,
        };
    }

    private static bool IsOpenTelemetryPackage(string package) =>
        package.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase)
        || package.StartsWith("Azure.Monitor.OpenTelemetry", StringComparison.OrdinalIgnoreCase);

    private static bool IsApplicationInsightsPackage(string package) =>
        package.StartsWith("Microsoft.ApplicationInsights", StringComparison.OrdinalIgnoreCase);

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
