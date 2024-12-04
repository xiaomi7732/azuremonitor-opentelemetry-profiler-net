//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface IProfilerFrontendClientFactory
{
    IProfilerFrontendClient CreateProfilerFrontendClient();
}
