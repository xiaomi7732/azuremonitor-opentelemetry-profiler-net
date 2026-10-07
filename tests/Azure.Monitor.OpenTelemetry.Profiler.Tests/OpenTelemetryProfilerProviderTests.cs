//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System.Reflection;
using Azure.Monitor.OpenTelemetry.Profiler.Core;
using Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Azure.Monitor.OpenTelemetry.Profiler.Tests;

/// <summary>
/// Regression tests for issue #191 covering how a failed start unwinds.
/// <para>
/// <c>IProfilerSource</c> and <c>IResourceUsageSource</c> live in
/// Microsoft.ServiceProfiler.Orchestration, which does not grant InternalsVisibleTo to this
/// assembly, so those two arguments are built by reflection rather than named directly.
/// </para>
/// </summary>
public class OpenTelemetryProfilerProviderTests
{
    [Fact]
    public async Task StartServiceProfilerAsync_WhenStartFailsAfterEnabling_TearsTheSessionDownAndFreesTheProfiler()
    {
        // A start can fail after the EventPipe session is already live - here the listener factory
        // throws. Releasing the profiling semaphore without disabling that session would leave it
        // streaming with nobody able to stop it (the normal stop path returns early once
        // IsProfilerRunning is false) and would block every later session.
        Mock<ITraceControl> traceControl = new();
        traceControl.Setup(t => t.DisableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        OpenTelemetryProfilerProvider target = CreateTarget(traceControl);

        await Assert.ThrowsAnyAsync<Exception>(() => StartAsync(target));

        // The live session was disabled rather than abandoned.
        traceControl.Verify(t => t.DisableAsync(It.IsAny<CancellationToken>()), Times.Once);

        // And the profiler is free again, so later sessions are not blocked for the process lifetime.
        Assert.False(target.IsProfilerRunning);
    }

    [Fact]
    public async Task StartServiceProfilerAsync_WhenEnablingItselfFails_DoesNotAttemptToDisable()
    {
        // Nothing was enabled, so there is no session to tear down - only the semaphore to free.
        Mock<ITraceControl> traceControl = new();
        traceControl
            .Setup(t => t.EnableAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        OpenTelemetryProfilerProvider target = CreateTarget(traceControl);

        await Assert.ThrowsAsync<InvalidOperationException>(() => StartAsync(target));

        traceControl.Verify(t => t.DisableAsync(It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(target.IsProfilerRunning);
    }

    private static async Task StartAsync(OpenTelemetryProfilerProvider target)
    {
        MethodInfo start = typeof(OpenTelemetryProfilerProvider)
            .GetMethod(nameof(OpenTelemetryProfilerProvider.StartServiceProfilerAsync))!;
        Type profilerSourceType = start.GetParameters()[0].ParameterType;

        await (Task<bool>)start.Invoke(target, [CreateMock(profilerSourceType), CancellationToken.None])!;
    }

    /// <summary>
    /// Builds a provider whose listener factory is backed by an empty container, so
    /// <see cref="TraceSessionListenerFactory.Create"/> throws once the trace control has enabled.
    /// </summary>
    private static OpenTelemetryProfilerProvider CreateTarget(Mock<ITraceControl> traceControl)
    {
        Mock<IUserCacheManager> userCacheManager = new();
        userCacheManager.SetupGet(u => u.TempTraceDirectory).Returns(new DirectoryInfo(Path.GetTempPath()));

        ServiceProvider emptyServiceProvider = new ServiceCollection().BuildServiceProvider();

        ConstructorInfo constructor = typeof(OpenTelemetryProfilerProvider).GetConstructors().Single();
        Type resourceUsageSourceType = constructor.GetParameters()
            .Single(p => p.Name == "resourceUsageSource").ParameterType;

        return (OpenTelemetryProfilerProvider)constructor.Invoke(
        [
            traceControl.Object,
            userCacheManager.Object,
            new TraceSessionListenerFactory(emptyServiceProvider),
            Mock.Of<IPostStopProcessorFactory>(),
            Mock.Of<IServiceProfilerContext>(),
            CreateMock(resourceUsageSourceType),
            NullLogger<OpenTelemetryProfilerProvider>.Instance,
        ]);
    }

    private static object CreateMock(Type type)
    {
        Mock mock = (Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(type))!;
        return mock.Object;
    }
}
