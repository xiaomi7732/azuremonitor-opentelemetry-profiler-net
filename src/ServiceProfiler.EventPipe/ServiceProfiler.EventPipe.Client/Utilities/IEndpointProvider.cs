//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;

namespace Microsoft.ApplicationInsights.Profiler.Core
{
    internal interface IEndpointProvider
    {
        string ConnectionString { get; set; }

        string GetInstrumentationKey();

        Uri GetEndpoint(EndpointName endpointName);
    }
}
