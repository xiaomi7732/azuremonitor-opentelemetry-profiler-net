// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using ServiceProfiler.Common.Utilities;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface IServiceProfilerContext
{
    ConnectionString? ConnectionString { get; }

    /// <summary>
    /// Gets the raw connection string value as provided by the user, before parsing.
    /// This is null when no connection string was provided, and may be a non-empty but
    /// unparsable value when the connection string is malformed.
    /// </summary>
    string? ConnectionStringValue { get; }

    Guid AppInsightsInstrumentationKey { get; }

    string MachineName { get; }

    Uri StampFrontendEndpointUrl { get; }
}
