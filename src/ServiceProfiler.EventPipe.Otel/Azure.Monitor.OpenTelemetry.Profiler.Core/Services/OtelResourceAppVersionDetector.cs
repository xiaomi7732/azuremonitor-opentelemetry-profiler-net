//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.Services;

/// <summary>
/// Detects the application version from the OpenTelemetry Resource <c>service.version</c> attribute.
/// </summary>
internal sealed class OtelResourceAppVersionDetector : IAppVersionDetector
{
    private readonly OtelResourceDetector _otelResourceDetector;

    public OtelResourceAppVersionDetector(OtelResourceDetector otelResourceDetector)
    {
        _otelResourceDetector = otelResourceDetector ?? throw new ArgumentNullException(nameof(otelResourceDetector));
    }

    public string? GetAppVersion()
        => _otelResourceDetector.GetResource(OtelResourceSemanticConventions.AttributeServiceVersion);
}
