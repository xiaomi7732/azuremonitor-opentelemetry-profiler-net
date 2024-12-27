// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

/// <summary>
/// A utility class to get the current process information.
/// This is a simple helper to manage the dispose of process object after use.
/// </summary>
internal class CurrentProcessUtilities : ITargetProcess
{
    /// <inheritdoc />
    public int ProcessId { get; } = GetId();

    /// <summary>
    /// Gets the unique identifier for the associated process.
    /// </summary>
    /// <exception cref="InvalidOperationException">The process's Id property has not been set or there is no process associated with this Process object.</exception>
    /// <returns>The process id of the current process.</returns>
    private static int GetId()
    {
        using Process currentProcess = Process.GetCurrentProcess();
        return currentProcess.Id;
    }

    /// <inheritdoc />
    /// <remarks>
    /// For current process, its always available at the time this class is called.
    /// </remarks>
    public Task<int> WaitUntilAvailableAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(ProcessId);
    }
}
