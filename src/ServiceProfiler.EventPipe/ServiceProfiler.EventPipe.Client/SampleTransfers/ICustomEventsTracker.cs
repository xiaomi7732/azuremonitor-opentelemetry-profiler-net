//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;

namespace Microsoft.ApplicationInsights.Profiler.Core.SampleTransfers
{
    internal interface ICustomEventsTracker
    {
        /// <summary>
        /// Send samples to Application Insights
        /// </summary>
        /// <param name="samples">The sample collection.</param>
        /// <param name="uploadContext">The upload conext used to upload the trace.</param>
        /// <param name="processId">The process id.</param>
        /// <param name="profilingSource">The source for the profiling.</param>
        /// <param name="verifiedDataCube">AppId exchanged by the instrumentation key.</param>
        /// <returns>Returns the sample count that has been uploaded.</returns>
        int Send(IEnumerable<SampleActivity> samples, UploadContext uploadContext, int processId, string profilingSource, Guid verifiedDataCube);
    }
}
