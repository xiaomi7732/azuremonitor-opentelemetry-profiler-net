// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface IServiceProfilerContext
{
    // Guid AppInsightsAppId { get; }

    string? ConnectionString { get; }

    Guid AppInsightsInstrumentationKey { get; }

    string MachineName { get; }

    Uri StampFrontendEndpointUrl { get; }
}
