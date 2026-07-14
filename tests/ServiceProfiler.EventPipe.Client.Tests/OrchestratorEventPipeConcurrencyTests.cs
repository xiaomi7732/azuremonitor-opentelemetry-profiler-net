// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task StartProfilingAsync_WhenGateHeldByAnotherStart_WarningNamesInFlightOperationNotNull()
    {
        // Regression test for issue #164: while one start holds the policy-change gate (and has not
        // yet assigned _currentProfilingPolicy), a second start that times out on the gate must
        // report the in-flight operation - not "(null)".
        TaskCompletionSource startEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseStart = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Mock<IServiceProfilerProvider> provider = new();
        provider.Setup(p => p.StartServiceProfilerAsync(It.IsAny<IProfilerSource>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                // Signal that the gate is now held, then block so it stays held.
                startEntered.TrySetResult();
                return releaseStart.Task;
            });

        Mock<IAsyncDisposable> leaseHandle = new();
        leaseHandle.Setup(l => l.DisposeAsync()).Returns(default(ValueTask));
        Mock<IProfilerConcurrencyControlClient> concurrency = new();
        concurrency.Setup(c => c.TryAcquireLeaseAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(leaseHandle.Object);

        CapturingLogger logger = new();
        TestOrchestrator orchestrator = CreateOrchestrator(provider, concurrency, logger);

        TestPolicy holder = new("HoldingPolicy");
        TestPolicy waiter = new("WaitingPolicy");

        Task<bool> holderTask = orchestrator.StartProfilingAsync(holder, CancellationToken.None);
        await startEntered.Task; // The gate is now held by the holder start.

        // This call cannot acquire the gate within its 500ms timeout and logs the conflict warning.
        bool waiterResult = await orchestrator.StartProfilingAsync(waiter, CancellationToken.None);

        // Let the holder finish.
        releaseStart.SetResult(true);
        Assert.True(await holderTask);

        Assert.False(waiterResult);

        string warning = Assert.Single(logger.Entries.Where(e => e.Level == LogLevel.Warning)).Message;
        Assert.Contains("WaitingPolicy", warning);           // who was denied
        Assert.Contains("start by HoldingPolicy", warning);  // the in-flight operation
        Assert.DoesNotContain("(null)", warning);
    }

    private static TestOrchestrator CreateOrchestrator(
        Mock<IServiceProfilerProvider> provider,
        Mock<IProfilerConcurrencyControlClient> concurrency,
        ILogger<OrchestratorEventPipe> logger = null)
    {
        IOptions<UserConfigurationBase> options = Options.Create<UserConfigurationBase>(new TestUserConfiguration());
        return new TestOrchestrator(
            provider.Object,
            options,
            Mock.Of<IDelaySource>(),
            Mock.Of<IAgentStatusService>(),
            Mock.Of<IResourceUsageSource>(),
            concurrency.Object,
            logger ?? NullLogger<OrchestratorEventPipe>.Instance);
    }

    private sealed class CapturingLogger : ILogger<OrchestratorEventPipe>
    {
        private readonly object _gate = new();
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            lock (_gate)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
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
            IProfilerConcurrencyControlClient concurrencyControlClient,
            ILogger<OrchestratorEventPipe> logger)
            : base(
                profilerProvider,
                config,
                Array.Empty<SchedulingPolicy>(),
                delaySource,
                agentStatusService,
                resourceUsageSource,
                logger,
                concurrencyControlClient)
        {
        }
    }

    private sealed class TestPolicy : SchedulingPolicy
    {
        private readonly string _source;

        public TestPolicy(string source = nameof(TestPolicy))
            : base(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, Mock.Of<IDelaySource>(), Mock.Of<IExpirationPolicy>(), NullLogger<SchedulingPolicy>.Instance)
        {
            _source = source;
        }

        public override string Source => _source;

        public override IAsyncEnumerable<(TimeSpan duration, ProfilerAction action)> GetScheduleAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
