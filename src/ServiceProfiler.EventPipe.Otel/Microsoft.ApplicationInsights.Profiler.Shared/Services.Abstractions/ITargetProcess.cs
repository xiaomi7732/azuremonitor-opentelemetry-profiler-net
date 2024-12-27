//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface ITargetProcess
{
    /// <summary>
    /// Gets the process id of the target application.
    /// </summary>
    int ProcessId { get; }
}