//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

/// <summary>
/// Detects the application version by a single method. A collection of detectors can be registered
/// in the ServiceCollection and the first one that returns a non-empty value is used.
/// See <see cref="AggregatedAppVersionSource"/> for the usage.
/// </summary>
internal interface IAppVersionDetector
{
    /// <summary>
    /// Gets the application version, or <see langword="null"/>/empty if it cannot be determined.
    /// </summary>
    string? GetAppVersion();
}
