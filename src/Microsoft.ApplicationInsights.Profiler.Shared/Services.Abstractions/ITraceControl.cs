//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface ITraceControl
{
    /// <summary>
    /// Disables the current profiler session and waits for the trace file to finish being written.
    /// </summary>
    /// <returns>
    /// True when the trace file was written completely and is safe to process; false when the write
    /// did not finish (for example it timed out or failed), in which case the trace file is
    /// incomplete and must not be uploaded.
    /// <para>
    /// How thoroughly completeness is established is up to the implementation: the OTel control
    /// verifies that the stream ended after the session was asked to stop and that the file carries
    /// the nettrace end-of-stream marker, while the classic control only reports whether the copy
    /// ran without error.
    /// </para>
    /// </returns>
    /// <exception cref="System.TimeoutException">Throws when timed out fetching the semaphore of the operation.</exception>
    Task<bool> DisableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Enables a profiler session.
    /// </summary>
    /// <remark>
    /// Only 1 profiler session is supported at a time.
    /// </remark>
    Task EnableAsync(string traceFilePath, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the session start time
    /// </summary>
    DateTime? SessionStartUTC { get; }
}