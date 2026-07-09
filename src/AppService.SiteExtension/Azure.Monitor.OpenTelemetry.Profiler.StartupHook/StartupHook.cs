// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Loader;
using Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

/// <summary>
/// Runtime startup hook (activated via <c>DOTNET_STARTUP_HOOKS</c>) whose job is to make the codeless
/// profiler payload loadable AND to isolate the two profiler stacks from each other.
///
/// The site extension stages a stack-agnostic router plus this hook at the payload root, and each profiler
/// stack's full closure in its own subfolder (<c>otel\</c> / <c>classic\</c>) with its own dependency
/// versions. This hook detects the application's telemetry stack up front (a dependency-free
/// <c>*.deps.json</c> scan, shared with the router as linked source) and installs an
/// <see cref="AssemblyLoadContext.Resolving"/> fallback scoped to <b>only</b> the matching stack's subfolder
/// plus the payload root. The other stack's subfolder is never on the probe path, so the two stacks'
/// dependencies are never unified. The decision is recorded via <c>AppContext</c> so the router activates the
/// same stack.
///
/// The payload is built against the lowest supported framework baseline (.NET 8 / Microsoft.Extensions.*
/// 8.0), so its references roll forward to whatever the target app already loaded (>= 8.0). The resolver
/// only fills genuine gaps - the app's own assemblies always win when present at an equal-or-higher version.
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
            string? payloadRoot = Path.GetDirectoryName(typeof(StartupHook).Assembly.Location);
            if (string.IsNullOrEmpty(payloadRoot) || !Directory.Exists(payloadRoot))
            {
                Log("Could not determine the payload directory; assembly resolver not installed.");
                return;
            }

            TelemetryStack stack = DetectStack();
            // Record the decision so the router (a separate assembly) activates the same stack. A string is
            // used because the two assemblies compile independent copies of the enum type.
            AppContext.SetData(DetectedStackAppContextData.Key, stack.ToString());
            Log("Detected telemetry stack: " + stack);

            List<string> probeDirectories = BuildProbeDirectories(payloadRoot!, stack);

            AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
            {
                try
                {
                    if (string.IsNullOrEmpty(assemblyName.Name))
                    {
                        return null;
                    }

                    foreach (string directory in probeDirectories)
                    {
                        string candidate = Path.Combine(directory, assemblyName.Name + ".dll");
                        if (File.Exists(candidate))
                        {
                            return context.LoadFromAssemblyPath(candidate);
                        }
                    }

                    return null;
                }
                catch
                {
                    // Never let a resolution attempt crash the host.
                    return null;
                }
            };

            Log("Assembly resolver installed. Probe order: " + string.Join(", ", probeDirectories));
        }
        catch (Exception ex)
        {
            Log("Failed to install the assembly resolver: " + ex);
        }
    }

    /// <summary>
    /// Builds the resolver probe order: the detected stack's subfolder first (so that stack's own closure
    /// wins for everything it ships), then the payload root (for the router and anything the subfolder does
    /// not carry). The other stack's subfolder is deliberately excluded, keeping the two stacks isolated.
    /// </summary>
    private static List<string> BuildProbeDirectories(string payloadRoot, TelemetryStack stack)
    {
        List<string> directories = new();

        string subfolder = DetectedStackAppContextData.ToPayloadSubfolder(stack);
        if (!string.IsNullOrEmpty(subfolder))
        {
            string stackDirectory = Path.Combine(payloadRoot, subfolder);
            if (Directory.Exists(stackDirectory))
            {
                directories.Add(stackDirectory);
            }
            else
            {
                Log("Expected payload subfolder not found (the stack will not be resolvable): " + stackDirectory);
            }
        }

        directories.Add(payloadRoot);
        return directories;
    }

    private static TelemetryStack DetectStack()
    {
        try
        {
            return new DepsFileTelemetryStackDetector().Detect();
        }
        catch (Exception ex)
        {
            Log("Telemetry stack detection failed; treating as None. " + ex);
            return TelemetryStack.None;
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
