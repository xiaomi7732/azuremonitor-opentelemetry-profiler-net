// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Monitor.Diagnostics;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Azure.Monitor.OpenTelemetry.Profiler.Tests;

public class ProfilerConcurrencyControlClientTests
{
    private static ProfilerConcurrencyControlClient CreateClient(IProfilerLeaseClient leaseClient)
    {
        Mock<IServiceProfilerContext> context = new();
        context.SetupGet(c => c.StampFrontendEndpointUrl).Returns(new Uri("https://example.com/"));

        Mock<IRoleNameSource> roleName = new();
        roleName.SetupGet(r => r.CloudRoleName).Returns("role");

        Mock<IRoleInstanceSource> roleInstance = new();
        roleInstance.SetupGet(r => r.CloudRoleInstance).Returns("instance");

        return new ProfilerConcurrencyControlClient(
            leaseClient,
            context.Object,
            roleName.Object,
            roleInstance.Object,
            NullLoggerFactory.Instance,
            NullLogger<ProfilerConcurrencyControlClient>.Instance);
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_WhenAcquired_ReturnsLease()
    {
        Mock<IProfilerLeaseClient> lease = new();
        lease.Setup(l => l.AcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        ProfilerConcurrencyControlClient client = CreateClient(lease.Object);

        IAsyncDisposable? result = await client.TryAcquireLeaseAsync(CancellationToken.None);

        Assert.NotNull(result);
        await result!.DisposeAsync();
        lease.Verify(
            l => l.AcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_WhenCapReached_ReturnsNull()
    {
        Mock<IProfilerLeaseClient> lease = new();
        lease.Setup(l => l.AcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LeaseUnavailableException("cap reached"));

        ProfilerConcurrencyControlClient client = CreateClient(lease.Object);

        IAsyncDisposable? result = await client.TryAcquireLeaseAsync(CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_WhenTransientError_FailsOpen()
    {
        Mock<IProfilerLeaseClient> lease = new();
        lease.Setup(l => l.AcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("server down"));

        ProfilerConcurrencyControlClient client = CreateClient(lease.Object);

        IAsyncDisposable? result = await client.TryAcquireLeaseAsync(CancellationToken.None);

        // Fail-open: a non-cap error must not block profiling.
        Assert.Same(NoOpProfilerConcurrencyControlClient.GrantedLease, result);
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_WhenCancelled_Propagates()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        Mock<IProfilerLeaseClient> lease = new();
        lease.Setup(l => l.AcquireAsync(It.IsAny<TimeSpan>(), It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        ProfilerConcurrencyControlClient client = CreateClient(lease.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => client.TryAcquireLeaseAsync(cts.Token));
    }
}
