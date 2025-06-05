//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;

namespace Microsoft.ApplicationInsights.Profiler.Core.UploaderProxy
{
    /// <summary>
    /// Provides functions to call an external executable.
    /// </summary>
    public interface IOutOfProcCaller
    {
        /// <summary>
        /// Setups a configure used to call an executable out of process.
        /// </summary>
        /// <param name="fileName">The executable.</param>
        /// <param name="arguments">The arguments as a line of string.</param>
        void Setup(string fileName, string arguments);

        /// <summary>
        /// Executes the configured executable.
        /// </summary>
        [Obsolete("This will be removed in the future. Use ExecuteAndWait() instead.", error: true)]
        Process Execute(ProcessPriorityClass processPriorityClass = ProcessPriorityClass.Normal);

        /// <summary>
        /// Execute the configured executable, wait until the process finished and return the exit code.
        /// </summary>
        int ExecuteAndWait(ProcessPriorityClass processPriorityClass = ProcessPriorityClass.Normal);
    }
}
