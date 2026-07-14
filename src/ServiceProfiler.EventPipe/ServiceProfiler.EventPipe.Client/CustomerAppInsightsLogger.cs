//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using Microsoft.ApplicationInsights.Profiler.Core.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Core.Orchestration;

/// <summary>
/// Holds the <see cref="IAppInsightsLogger"/> that targets the <b>customer's</b> Application
/// Insights resource (as opposed to Microsoft's anonymous-telemetry resource). Used to forward
/// telemetry that must reach the customer only, e.g. uploader sub-process logs.
/// </summary>
internal sealed class CustomerAppInsightsLogger
{
    public CustomerAppInsightsLogger(IAppInsightsLogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IAppInsightsLogger Logger { get; }
}
