//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface ITargetProcess
{
    /// <summary>
    /// Gets the process id of the target application.
    /// </summary>
    int ProcessId { get; }

    /// <summary>
    /// Wait until the target process exist.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The process id.</returns>
    Task<int> WaitUntilAvailableAsync(CancellationToken cancellationToken);
}