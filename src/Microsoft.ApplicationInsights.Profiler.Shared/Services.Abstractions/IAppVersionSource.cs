//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

/// <summary>
/// Provides the effective application version to associate with profiler telemetry.
/// </summary>
internal interface IAppVersionSource
{
    /// <summary>
    /// Gets the application version, or <see cref="string.Empty"/> when it cannot be determined.
    /// </summary>
    string AppVersion { get; }
}
