// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

/// <summary>
/// Runtime startup hook (activated via <c>DOTNET_STARTUP_HOOKS</c>) whose only job is to make the
/// codeless profiler payload loadable. The site extension stages the profiler assemblies in a folder
/// that is NOT on the application's normal probing path, so this hook installs an
/// <see cref="AssemblyLoadContext.Resolving"/> fallback that loads any otherwise-unresolved assembly
/// from the payload folder (the directory this hook lives in).
///
/// It must remain dependency-free (BCL only) so it can load before anything else, and it resolves only
/// as a fallback - the application's own assemblies always win, so bundled copies merely fill gaps.
/// The class name and signature (<c>StartupHook.Initialize()</c>, no namespace) are mandated by the runtime.
/// </summary>
internal sealed class StartupHook
{
    private const string LogPrefix = "[Azure.Monitor.OpenTelemetry.Profiler.StartupHook] ";

    public static void Initialize()
    {
        try
        {
            string? payloadDirectory = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location);
            if (string.IsNullOrEmpty(payloadDirectory) || !Directory.Exists(payloadDirectory))
            {
                Log("Could not determine the payload directory; assembly resolver not installed.");
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

            Log("Assembly resolver installed for payload directory: " + payloadDirectory);
        }
        catch (Exception ex)
        {
            Log("Failed to install the assembly resolver: " + ex);
        }
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
