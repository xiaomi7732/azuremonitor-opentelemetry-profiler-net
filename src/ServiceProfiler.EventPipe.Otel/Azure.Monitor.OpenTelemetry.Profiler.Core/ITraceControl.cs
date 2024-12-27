//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

public interface ITraceControl
{
        /// <summary>
        /// Disables the current profiler session.
        /// </summary>
        /// <exception cref="TimeoutException">Throws when timed out fetching the semaphore of the operation.</exception>
        /// <returns>The target process id.</returns>
        Task<int> DisableAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Enables a profiler session.
        /// </summary>
        /// <remark>
        /// Only 1 profiler session is supported at a time.
        /// </remark>
        Task EnableAsync(string traceFilePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the session start time
        /// </summary>
        DateTime? SessionStartUTC { get; }
}