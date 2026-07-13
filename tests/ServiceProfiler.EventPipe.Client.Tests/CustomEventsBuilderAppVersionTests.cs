// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ServiceProfiler.Orchestration;
using Moq;
using Xunit;

namespace ServiceProfiler.EventPipe.Client.Tests;

public class CustomEventsBuilderAppVersionTests
{
    [Fact]
    public void CreateServiceProfilerIndex_SetsAppVersion()
    {
        CustomEventsBuilder builder = CreateBuilder("4.5.6");

        ServiceProfilerIndex index = builder.CreateServiceProfilerIndex(
            stampId: "stamp",
            sessionId: DateTimeOffset.UtcNow,
            appId: Guid.NewGuid(),
            artifactId: Guid.NewGuid(),
            profilerSource: Mock.Of<IProfilerSource>(s => s.Source == "TestSource"),
            averageCPUUsage: 1,
            averageMemoryUsage: 2);

        Assert.Equal("4.5.6", index.AppVersion);
    }

    [Fact]
    public void CreateServiceProfilerSamples_SetsAppVersion()
    {
        CustomEventsBuilder builder = CreateBuilder("4.5.6");

        SampleActivity sample = new()
        {
            StartActivityIdPath = "/1/",
            StopActivityIdPath = "/1/",
            StartTimeUtc = DateTimeOffset.UtcNow,
            StopTimeUtc = DateTimeOffset.UtcNow.AddSeconds(1),
            RequestId = "req-1",
            OperationId = "op-1",
            OperationName = "GET /",
        };

        ServiceProfilerSample result = builder.CreateServiceProfilerSamples(
            new[] { sample }, "stamp", DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid()).Single();

        Assert.Equal("4.5.6", result.AppVersion);
    }

    private static CustomEventsBuilder CreateBuilder(string appVersion)
    {
        Mock<IAppVersionSource> appVersionSource = new();
        appVersionSource.Setup(v => v.AppVersion).Returns(appVersion);

        return new CustomEventsBuilder(
            Mock.Of<IServiceProfilerContext>(),
            Mock.Of<IRoleNameSource>(s => s.CloudRoleName == "role"),
            Mock.Of<IRoleInstanceSource>(s => s.CloudRoleInstance == "instance"),
            appVersionSource.Object,
            NullLogger<CustomEventsBuilder>.Instance);
    }
}
