// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Diagnostics;

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

/// <summary>
/// Minimal, dependency-free logger used before the application's logging pipeline exists (the
/// HostingStartup runs while the host is still being built). Writes to stdout so App Service log
/// streaming picks it up, and mirrors to <see cref="Trace"/> for local debugging. Never throws.
/// </summary>
internal static class BootstrapLog
{
    private const string Prefix = "[Azure.Monitor.OpenTelemetry.Profiler.HostingStartup] ";

    public static void Info(string message) => Write("info", message);

    public static void Error(string message, Exception? exception = null) =>
        Write("error", exception is null ? message : $"{message} {exception}");

    private static void Write(string level, string message)
    {
        string line = $"{Prefix}{level}: {message}";
        try
        {
            Console.WriteLine(line);
        }
        catch
        {
            // Logging must never break host startup.
        }

        try
        {
            Trace.WriteLine(line);
        }
        catch
        {
            // Ditto.
        }
    }
}
