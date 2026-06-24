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
    /// Gets the result of validating the configured connection string. This intentionally does
    /// not expose the raw connection string value, so callers cannot misuse or leak it.
    /// </summary>
    ConnectionStringValidationResult ConnectionStringValidation { get; }

    Guid AppInsightsInstrumentationKey { get; }

    string MachineName { get; }

    Uri StampFrontendEndpointUrl { get; }
}
