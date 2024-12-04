//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Frontend;

public class RateLimitedException : Exception
{
    public RateLimitedException() { }

    public RateLimitedException(string message) : base(message) { }
}

