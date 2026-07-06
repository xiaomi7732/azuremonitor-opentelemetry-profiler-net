// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

/// <summary>
/// Runtime startup hook (activated via <c>DOTNET_STARTUP_HOOKS</c>) whose only job is to make the
/// codeless profiler payload loadable. The site extension stages the profiler assemblies in per-target-
/// framework subfolders (<c>net8.0/</c>, <c>net9.0/</c>, <c>net10.0/</c>) next to this hook, none of which
/// are on the application's normal probing path. This hook selects the subfolder matching the running
/// runtime major and installs an <see cref="AssemblyLoadContext.Resolving"/> fallback that loads any
/// otherwise-unresolved assembly from it (the application's own assemblies always win, so bundled copies
/// merely fill gaps).
///
/// It must remain dependency-free (BCL only) so it can load before anything else. The class name and
/// signature (<c>StartupHook.Initialize()</c>, no namespace) are mandated by the runtime.
/// </summary>
internal sealed class StartupHook
{
    private const string LogPrefix = "[Azure.Monitor.OpenTelemetry.Profiler.StartupHook] ";

    public static void Initialize()
    {
        try
        {
            string? rootDirectory = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location);
            if (string.IsNullOrEmpty(rootDirectory) || !Directory.Exists(rootDirectory))
            {
                Log("Could not determine the payload directory; assembly resolver not installed.");
                return;
            }

            int runtimeMajor = Environment.Version.Major;
            string? payloadDirectory = SelectPayloadDirectory(rootDirectory!, runtimeMajor);
            if (string.IsNullOrEmpty(payloadDirectory))
            {
                Log($"No per-framework payload folder found under '{rootDirectory}' for runtime major " +
                    $"{runtimeMajor}; assembly resolver not installed.");
                return;
            }

            AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
            {
                try
                {
                    if (string.IsNullOrEmpty(assemblyName.Name))
                    {
                        return null;
                    }

                    string candidate = Path.Combine(payloadDirectory!, assemblyName.Name + ".dll");
                    return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
                }
                catch
                {
                    // Never let a resolution attempt crash the host.
                    return null;
                }
            };

            Log($"Assembly resolver installed for runtime major {runtimeMajor}, payload directory: " + payloadDirectory);
        }
        catch (Exception ex)
        {
            Log("Failed to install the assembly resolver: " + ex);
        }
    }

    /// <summary>
    /// Chooses the <c>net{major}.0</c> payload subfolder under <paramref name="rootDirectory"/> for the
    /// given runtime major version. Prefers an exact match; otherwise falls back to the highest available
    /// framework folder whose major is less than or equal to the runtime (so a future runtime uses the
    /// newest bundled payload). Returns <c>null</c> when no suitable folder exists.
    /// </summary>
    internal static string? SelectPayloadDirectory(string rootDirectory, int runtimeMajor)
    {
        string exact = Path.Combine(rootDirectory, "net" + runtimeMajor + ".0");
        if (Directory.Exists(exact))
        {
            return exact;
        }

        int bestMajor = -1;
        string? best = null;
        foreach (string dir in Directory.GetDirectories(rootDirectory))
        {
            string name = Path.GetFileName(dir);
            if (name.StartsWith("net", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".0", StringComparison.OrdinalIgnoreCase))
            {
                string majorText = name.Substring(3, name.Length - 3 - 2);
                if (int.TryParse(majorText, out int major) && major <= runtimeMajor && major > bestMajor)
                {
                    bestMajor = major;
                    best = dir;
                }
            }
        }

        return best;
    }

    private static void Log(string message)
    {
        try
        {
            Console.WriteLine(LogPrefix + message);
        }
        catch
        {
            // Logging must never break host startup.
        }
    }
}
