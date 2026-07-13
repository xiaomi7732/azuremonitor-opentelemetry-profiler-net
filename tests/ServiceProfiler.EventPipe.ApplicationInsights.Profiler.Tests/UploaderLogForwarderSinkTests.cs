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
        public void Track_MapsLogLevelToSeverityAndForwardsToAllLoggers(LogLevel level, SeverityLevel expected)
        {
            Mock<IAppInsightsLogger> logger1 = new();
            Mock<IAppInsightsLogger> logger2 = new();
            var sink = new UploaderLogForwarderSink(new[] { logger1.Object, logger2.Object });

            sink.Track(level, "[Uploader] hello");

            logger1.Verify(l => l.TrackTrace("[Uploader] hello", expected, It.IsAny<IDictionary<string, string>>(), It.IsAny<bool>()), Times.Once);
            logger2.Verify(l => l.TrackTrace("[Uploader] hello", expected, It.IsAny<IDictionary<string, string>>(), It.IsAny<bool>()), Times.Once);
        }
    }
}
