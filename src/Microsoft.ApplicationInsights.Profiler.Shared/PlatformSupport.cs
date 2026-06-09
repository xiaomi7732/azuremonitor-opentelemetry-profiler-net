// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Runtime.InteropServices;

namespace Microsoft.ApplicationInsights.Profiler.Shared;

/// <summary>
/// Provides platform compatibility checks for the profiler.
/// </summary>
internal static class PlatformSupport
{
    /// <summary>
    /// Gets whether the current OS platform is supported for profiling.
    /// Currently only Windows and Linux are supported.
    /// </summary>
    public static bool IsSupportedPlatform
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        || RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
}
