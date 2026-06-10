// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Shared;

/// <summary>
/// Provides platform compatibility checks for the profiler.
/// </summary>
internal static class PlatformSupport
{
    /// <summary>
    /// Returns whether the current OS platform is supported for profiling.
    /// Currently only Windows and Linux are supported.
    /// When the platform is unsupported and a logger is provided, a warning is emitted.
    /// </summary>
    public static bool IsSupportedPlatform(ILogger? logger = null)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return true;
        }

        logger?.LogWarning(
            "Application Insights Profiler is not supported on the current OS platform ({OSDescription}). The profiler will be disabled.",
            RuntimeInformation.OSDescription);

        if (logger is null)
        {
            Console.WriteLine($"[Warning] Application Insights Profiler is not supported on the current OS platform ({RuntimeInformation.OSDescription}). The profiler will be disabled.");
        }
        return false;
    }
}
