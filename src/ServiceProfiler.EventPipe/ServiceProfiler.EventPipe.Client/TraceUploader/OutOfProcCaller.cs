//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Core.UploaderProxy
{
    public class OutOfProcCaller : IOutOfProcCaller
    {
        public OutOfProcCaller(ILogger<OutOfProcCaller> logger)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string FileName { get; private set; }
        public string Arguments { get; private set; }

        [Obsolete("Use ExecuteAndWait instead.", error: true)]
        public virtual Process Execute(ProcessPriorityClass processPriorityClass = ProcessPriorityClass.Normal)
        {
            return ExecuteImp(processPriorityClass);
        }

        public int ExecuteAndWait(ProcessPriorityClass processPriorityClass = ProcessPriorityClass.Normal)
        {
            using Process p = ExecuteImp(processPriorityClass);
            p.WaitForExit();
            return p.ExitCode;
        }

        public void Setup(string fileName, string arguments)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                throw new ArgumentException($"'{nameof(fileName)}' cannot be null or empty", nameof(fileName));
            }

            FileName = fileName;

            this.Arguments = arguments;
        }

        private Process ExecuteImp(ProcessPriorityClass processPriorityClass)
        {
            Logger.LogDebug("Calling execute out of proc on {fileName} with arguments: {arguments}. Intended priority: {intendedPriority}", FileName, Arguments, processPriorityClass);
            Process process = Process.Start(FileName, Arguments);
            try
            {
                process.PriorityClass = processPriorityClass;
            }
            catch (Exception ex) when (
                ex is Win32Exception ||
                ex is NotSupportedException)
            {
                // This could happen in restrained environment like Antares.
                Logger.LogDebug(ex, "Can't set process priority to {intendedPriority}.", processPriorityClass);
            }

            return process;
        }

        protected ILogger Logger { get; private set; }
    }
}
