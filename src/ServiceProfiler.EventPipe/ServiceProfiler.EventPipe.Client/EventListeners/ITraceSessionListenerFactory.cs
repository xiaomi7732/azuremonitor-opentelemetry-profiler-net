//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.ApplicationInsights.Profiler.Core.EventListeners
{
    internal interface ITraceSessionListenerFactory
    {
        [Obsolete("Use CreateTraceSessionListeners instead.", error: false)]
        ITraceSessionListener CreateTraceSessionListener();

        IEnumerable<ITraceSessionListener> CreateTraceSessionListeners();
    }
}
