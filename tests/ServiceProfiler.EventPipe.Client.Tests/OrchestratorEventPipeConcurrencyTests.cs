// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Orchestration;
using Moq;
using Xunit;

namespace ServiceProfiler.EventPipe.Client.Tests;

public class OrchestratorEventPipeConcurrencyTests
{
    [Fact]
    public async Task StartProfilingAsync_WhenLeaseUnavailable_DoesNotStartProfiler()
    {
        Mock<IServiceProfilerProvider> provider = new();
        Mock<IProfilerConcurrencyControlClient> concurrency = new();
        concurrency.Setup(c => c.TryAcquireLeaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAsyncDisposable)null);

        TestOrchestrator orchestrator = CreateOrchestrator(provider, concurrency);
        TestPolicy policy = new();

        bool result = await orchestrator.StartProfilingAsync(policy, CancellationToken.None);

        Assert.False(result);
        provider.Verify(
            p => p.StartServiceProfilerAsync(It.IsAny<IProfilerSource>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task StartProfilingAsync_WhenLeaseGranted_StartsProfiler()
    {
        Mock<IServiceProfilerProvider> provider = new();
        provider.Setup(p => p.StartServiceProfilerAsync(It.IsAny<IProfilerSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Mock<IAsyncDisposable> leaseHandle = new();
        leaseHandle.Setup(l => l.DisposeAsync()).Returns(default(ValueTask));

        Mock<IProfilerConcurrencyControlClient> concurrency = new();
        concurrency.Setup(c => c.TryAcquireLeaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaseHandle.Object);

        TestOrchestrator orchestrator = CreateOrchestrator(provider, concurrency);
        TestPolicy policy = new();

        bool result = await orchestrator.StartProfilingAsync(policy, CancellationToken.None);

        Assert.True(result);
        provider.Verify(
            p => p.StartServiceProfilerAsync(policy, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StopProfilingAsync_ReleasesLease()
    {
        Mock<IServiceProfilerProvider> provider = new();
        provider.Setup(p => p.StartServiceProfilerAsync(It.IsAny<IProfilerSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        provider.Setup(p => p.StopServiceProfilerAsync(It.IsAny<IProfilerSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Mock<IAsyncDisposable> leaseHandle = new();
        leaseHandle.Setup(l => l.DisposeAsync()).Returns(default(ValueTask));

        Mock<IProfilerConcurrencyControlClient> concurrency = new();
        concurrency.Setup(c => c.TryAcquireLeaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaseHandle.Object);

        TestOrchestrator orchestrator = CreateOrchestrator(provider, concurrency);
        TestPolicy policy = new();

        Assert.True(await orchestrator.StartProfilingAsync(policy, CancellationToken.None));
        Assert.True(await orchestrator.StopProfilingAsync(policy, CancellationToken.None));

        leaseHandle.Verify(l => l.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task StartProfilingAsync_WhenProfilerFailsToStart_ReleasesLease()
    {
        Mock<IServiceProfilerProvider> provider = new();
        provider.Setup(p => p.StartServiceProfilerAsync(It.IsAny<IProfilerSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Mock<IAsyncDisposable> leaseHandle = new();
        leaseHandle.Setup(l => l.DisposeAsync()).Returns(default(ValueTask));

        Mock<IProfilerConcurrencyControlClient> concurrency = new();
        concurrency.Setup(c => c.TryAcquireLeaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaseHandle.Object);

        TestOrchestrator orchestrator = CreateOrchestrator(provider, concurrency);
        TestPolicy policy = new();

        bool result = await orchestrator.StartProfilingAsync(policy, CancellationToken.None);

        Assert.False(result);
        leaseHandle.Verify(l => l.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task StartProfilingAsync_WhenProviderThrowsWhileRunning_StopsAndReleasesLease()
    {
        Mock<IServiceProfilerProvider> provider = new();
        provider.Setup(p => p.StartServiceProfilerAsync(It.IsAny<IProfilerSource>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("threw after starting"));
        // The provider reports running (e.g. its semaphore is held) despite throwing.
        provider.SetupGet(p => p.IsProfilerRunning).Returns(true);
        provider.Setup(p => p.StopServiceProfilerAsync(It.IsAny<IProfilerSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        Mock<IAsyncDisposable> leaseHandle = new();
        leaseHandle.Setup(l => l.DisposeAsync()).Returns(default(ValueTask));

        Mock<IProfilerConcurrencyControlClient> concurrency = new();
        concurrency.Setup(c => c.TryAcquireLeaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaseHandle.Object);

        TestOrchestrator orchestrator = CreateOrchestrator(provider, concurrency);
        TestPolicy policy = new();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.StartProfilingAsync(policy, CancellationToken.None));

        // A failed start must never retain the lease: the profiler is best-effort stopped and the
        // lease released immediately, so it cannot be leaked regardless of provider state.
        provider.Verify(
            p => p.StopServiceProfilerAsync(It.IsAny<IProfilerSource>(), It.IsAny<CancellationToken>()),
            Times.Once);
        leaseHandle.Verify(l => l.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task StopProfilingAsync_WhenStopFailsDuringCancellation_ReleasesLease()
    {
        using CancellationTokenSource cts = new();

        Mock<IServiceProfilerProvider> provider = new();
        provider.Setup(p => p.StartServiceProfilerAsync(It.IsAny<IProfilerSource>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        // The stop is cancelled mid-flight (e.g. agent deactivation/shutdown) and throws while the
        // profiler still reports running. The token is cancelled as part of the call.
        provider.Setup(p => p.StopServiceProfilerAsync(It.IsAny<IProfilerSource>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new OperationCanceledException());
        provider.SetupGet(p => p.IsProfilerRunning).Returns(true);

        Mock<IAsyncDisposable> leaseHandle = new();
        leaseHandle.Setup(l => l.DisposeAsync()).Returns(default(ValueTask));

        Mock<IProfilerConcurrencyControlClient> concurrency = new();
        concurrency.Setup(c => c.TryAcquireLeaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaseHandle.Object);

        TestOrchestrator orchestrator = CreateOrchestrator(provider, concurrency);
        TestPolicy policy = new();

        Assert.True(await orchestrator.StartProfilingAsync(policy, CancellationToken.None));

        // The stop is cancelled and throws; because it was cancelled (terminal), the lease must be
        // released (best-effort stop first) instead of retained, so it cannot renew forever.
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => orchestrator.StopProfilingAsync(policy, cts.Token));

        leaseHandle.Verify(l => l.DisposeAsync(), Times.Once);
    }

    private static TestOrchestrator CreateOrchestrator(
        Mock<IServiceProfilerProvider> provider,
        Mock<IProfilerConcurrencyControlClient> concurrency)
    {
        IOptions<UserConfigurationBase> options = Options.Create<UserConfigurationBase>(new TestUserConfiguration());
        return new TestOrchestrator(
            provider.Object,
            options,
            Mock.Of<IDelaySource>(),
            Mock.Of<IAgentStatusService>(),
            Mock.Of<IResourceUsageSource>(),
            concurrency.Object);
    }

    private sealed class TestUserConfiguration : UserConfigurationBase
    {
    }

    private sealed class TestOrchestrator : OrchestratorEventPipe
    {
        public TestOrchestrator(
            IServiceProfilerProvider profilerProvider,
            IOptions<UserConfigurationBase> config,
            IDelaySource delaySource,
            IAgentStatusService agentStatusService,
            IResourceUsageSource resourceUsageSource,
            IProfilerConcurrencyControlClient concurrencyControlClient)
            : base(
                profilerProvider,
                config,
                Array.Empty<SchedulingPolicy>(),
                delaySource,
                agentStatusService,
                resourceUsageSource,
                NullLogger<OrchestratorEventPipe>.Instance,
                concurrencyControlClient)
        {
        }
    }

    private sealed class TestPolicy : SchedulingPolicy
    {
        public TestPolicy()
            : base(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, Mock.Of<IDelaySource>(), Mock.Of<IExpirationPolicy>(), NullLogger<SchedulingPolicy>.Instance)
        {
        }

        public override string Source => nameof(TestPolicy);

        public override IAsyncEnumerable<(TimeSpan duration, ProfilerAction action)> GetScheduleAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
