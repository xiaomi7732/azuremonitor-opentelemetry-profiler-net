//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Profiler.Core.Logging;
using Microsoft.ApplicationInsights.Profiler.Core.Orchestration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ServiceProfiler.EventPipe.Client.Tests
{
    public class UploaderLogForwarderSinkTests
    {
        [Theory]
        [InlineData(LogLevel.Trace, SeverityLevel.Verbose)]
        [InlineData(LogLevel.Debug, SeverityLevel.Verbose)]
        [InlineData(LogLevel.Information, SeverityLevel.Information)]
        [InlineData(LogLevel.Warning, SeverityLevel.Warning)]
        [InlineData(LogLevel.Error, SeverityLevel.Error)]
        [InlineData(LogLevel.Critical, SeverityLevel.Critical)]
        public void Track_MapsLogLevelToSeverityAndForwardsToCustomerLogger(LogLevel level, SeverityLevel expected)
        {
            Mock<IAppInsightsLogger> customerLogger = new();
            var sink = new UploaderLogForwarderSink(new CustomerAppInsightsLogger(customerLogger.Object));

            sink.Track(level, "[Uploader] hello");

            customerLogger.Verify(l => l.TrackTrace("[Uploader] hello", expected, It.IsAny<IDictionary<string, string>>(), It.IsAny<bool>()), Times.Once);
        }
    }
}
