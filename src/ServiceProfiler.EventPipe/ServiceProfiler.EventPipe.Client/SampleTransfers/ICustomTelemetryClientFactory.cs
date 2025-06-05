//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights;

namespace Microsoft.ApplicationInsights.Profiler.Core.SampleTransfers
{
    internal interface ICustomTelemetryClientFactory
    {
        TelemetryClient Create();
    }
}