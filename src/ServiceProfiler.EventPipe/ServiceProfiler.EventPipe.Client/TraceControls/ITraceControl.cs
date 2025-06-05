//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Core.TraceControls
{
    internal interface ITraceControl
    {
        /// <summary>
        /// Disables the current profiler session.
        /// </summary>
        /// <exception cref="System.TimeoutException">Throws when timed out fetching the semaphore of the operation.</exception>
        Task DisableAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Enables a profiler session.
        /// </summary>
        /// <exception cref="System.TimeoutException">Throws when timed out fetching the semaphore of the operation.</exception>
        /// <remark>
        /// Only 1 profiler session is supported at a time.
        /// </remark>
        void Enable(string traceFilePath = "default.nettrace");

        DateTime SessionStartUTC { get; }
    }
}
