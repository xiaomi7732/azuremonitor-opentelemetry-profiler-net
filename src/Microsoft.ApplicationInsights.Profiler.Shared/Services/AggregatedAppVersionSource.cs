//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

/// <summary>
/// Aggregates the registered <see cref="IAppVersionDetector"/>s and exposes the first non-empty
/// application version as the effective one (mirrors <see cref="AggregatedRoleNameSource"/>).
/// </summary>
internal class AggregatedAppVersionSource : IAppVersionSource
{
    private readonly ILogger<AggregatedAppVersionSource> _logger;

    public AggregatedAppVersionSource(
        IEnumerable<IAppVersionDetector> appVersionDetectors,
        ILogger<AggregatedAppVersionSource> logger)
    {
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));

        foreach (IAppVersionDetector detector in appVersionDetectors)
        {
            string appVersion = detector.GetAppVersion() ?? string.Empty;
            _logger.LogDebug("App version detector {detector} returned version: {appVersion}", detector.GetType().Name, appVersion);

            if (string.IsNullOrEmpty(appVersion))
            {
                // Try the next detector.
                continue;
            }

            // We have a non-empty version. This is the effective application version.
            AppVersion = appVersion;
            return;
        }

        _logger.LogDebug("Application version is effectively empty. No app version detector returned a non-empty value.");
        AppVersion = string.Empty;
    }

    public string AppVersion { get; }
}
