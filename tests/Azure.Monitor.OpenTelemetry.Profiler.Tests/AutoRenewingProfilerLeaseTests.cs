// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Azure.Monitor.OpenTelemetry.Profiler.Tests;

public class AutoRenewingProfilerLeaseTests
{
    [Fact]
    public async Task Lease_RenewsPeriodically_AndReleasesOnDispose()
    {
        Guid leaseId = Guid.NewGuid();
        using ManualResetEventSlim renewed = new(false);

        Mock<IProfilerLeaseClient> lease = new();
        lease.Setup(l => l.RenewAsync(leaseId, It.IsAny<CancellationToken>()))
            .Callback(() => renewed.Set())
            .Returns(Task.CompletedTask);
        lease.Setup(l => l.ReleaseAsync(leaseId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        AutoRenewingProfilerLease auto = new(lease.Object, leaseId, TimeSpan.FromMilliseconds(20), NullLogger.Instance);

        Assert.True(renewed.Wait(TimeSpan.FromSeconds(5)), "Expected the lease to be renewed at least once.");

        await auto.DisposeAsync();

        lease.Verify(l => l.ReleaseAsync(leaseId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Lease_RenewFailure_IsSwallowed_AndStillReleases()
    {
        Guid leaseId = Guid.NewGuid();
        using ManualResetEventSlim attempted = new(false);

        Mock<IProfilerLeaseClient> lease = new();
        lease.Setup(l => l.RenewAsync(leaseId, It.IsAny<CancellationToken>()))
            .Callback(() => attempted.Set())
            .ThrowsAsync(new InvalidOperationException("boom"));
        lease.Setup(l => l.ReleaseAsync(leaseId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        AutoRenewingProfilerLease auto = new(lease.Object, leaseId, TimeSpan.FromMilliseconds(20), NullLogger.Instance);

        Assert.True(attempted.Wait(TimeSpan.FromSeconds(5)), "Expected a renew attempt.");

        // Dispose must not throw even though renewal kept failing.
        await auto.DisposeAsync();

        lease.Verify(l => l.ReleaseAsync(leaseId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Lease_ReleaseFailure_DoesNotThrow()
    {
        Guid leaseId = Guid.NewGuid();

        Mock<IProfilerLeaseClient> lease = new();
        lease.Setup(l => l.RenewAsync(leaseId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        lease.Setup(l => l.ReleaseAsync(leaseId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("release failed"));

        AutoRenewingProfilerLease auto = new(lease.Object, leaseId, TimeSpan.FromSeconds(30), NullLogger.Instance);

        // Should complete without throwing.
        await auto.DisposeAsync();

        lease.Verify(l => l.ReleaseAsync(leaseId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Lease_WhenLeaseLostDuringRenew_StopsRenewing()
    {
        Guid leaseId = Guid.NewGuid();
        int renewCalls = 0;
        using ManualResetEventSlim lost = new(false);

        Mock<IProfilerLeaseClient> lease = new();
        lease.Setup(l => l.RenewAsync(leaseId, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                Interlocked.Increment(ref renewCalls);
                lost.Set();
            })
            .ThrowsAsync(new Azure.Monitor.Diagnostics.LeaseUnavailableException("lease lost"));
        lease.Setup(l => l.ReleaseAsync(leaseId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        AutoRenewingProfilerLease auto = new(lease.Object, leaseId, TimeSpan.FromMilliseconds(20), NullLogger.Instance);

        Assert.True(lost.Wait(TimeSpan.FromSeconds(5)), "Expected a renew attempt.");

        // Give the loop ample time to (incorrectly) retry; it must not after a lost lease.
        await Task.Delay(200);
        Assert.Equal(1, Volatile.Read(ref renewCalls));

        await auto.DisposeAsync();
        lease.Verify(l => l.ReleaseAsync(leaseId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
