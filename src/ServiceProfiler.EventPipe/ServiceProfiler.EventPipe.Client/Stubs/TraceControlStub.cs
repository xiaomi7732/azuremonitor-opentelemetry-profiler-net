//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ApplicationInsights.Profiler.Core.TraceControls;

namespace Microsoft.ApplicationInsights.Profiler.Core.Stubs
{
    internal class TraceControlStub : ITraceControl
    {
        ILogger _logger;
        public TraceControlStub(ILogger<TraceControlStub> logger)
        {
            _logger = logger;
        }

        public DateTime SessionStartUTC { get; private set; }

        public void Disable()
        {
            _logger.LogDebug("[{0:O}] {1} is disabled.", DateTime.Now, nameof(TraceControlStub));
        }

        public Task DisableAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void Enable(string traceFilePath)
        {
            SessionStartUTC = DateTime.UtcNow;
            _logger.LogDebug("[{0:O}] {1} is enabled at (UTC): {2:O}, filePath: {filePath}", DateTime.Now, nameof(TraceControlStub), SessionStartUTC, traceFilePath);
        }
    }
}
