//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

namespace Microsoft.ApplicationInsights.Profiler.Core.Utilities
{
    internal interface ICompatibilityUtility
    {
        (bool compatible, string reason) IsCompatible();
    }
}