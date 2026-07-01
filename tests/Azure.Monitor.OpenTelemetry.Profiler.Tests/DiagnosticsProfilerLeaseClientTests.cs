// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Monitor.Diagnostics;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Azure.Monitor.OpenTelemetry.Profiler.Tests;

/// <summary>
/// Seam-level tests for <see cref="DiagnosticsProfilerLeaseClient"/>. These exercise the real
/// translation logic against the raw SDK call, unlike <see cref="ProfilerConcurrencyControlClientTests"/>
/// which mock <see cref="IProfilerLeaseClient"/> to throw the typed exception directly. They pin the
/// contract the whole fail-open feature depends on: a cap-reached response on <c>acquire</c> must surface
/// as <see cref="LeaseUnavailableException"/> (so a cycle is skipped) rather than a generic exception
/// (which would fail open and silently defeat the concurrency cap).
/// </summary>
public class DiagnosticsProfilerLeaseClientTests
{
    private static readonly IReadOnlyDictionary<string, string> EmptyMetadata = new Dictionary<string, string>();

    /// <summary>
    /// Test seam: overrides the raw SDK acquire call so we can drive its outcome without a live backend.
    /// </summary>
    private sealed class FakeAcquireLeaseClient : DiagnosticsProfilerLeaseClient
    {
        private readonly Func<Task<Guid>> _acquire;

        public FakeAcquireLeaseClient(Func<Task<Guid>> acquire)
            : base(Mock.Of<IServiceProfilerContext>(), Mock.Of<IAuthTokenProvider>(), NullLoggerFactory.Instance)
        {
            _acquire = acquire;
        }

        internal override Task<Guid> AcquireCoreAsync(TimeSpan duration, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken)
            => _acquire();
    }

    [Fact]
    public async Task AcquireAsync_WhenCapReached409_ThrowsLeaseUnavailable()
    {
        // A 409 surfaced as a generic RequestFailedException on acquire must be translated.
        DiagnosticsProfilerLeaseClient client = new FakeAcquireLeaseClient(
            () => Task.FromException<Guid>(new RequestFailedException((int)System.Net.HttpStatusCode.Conflict, "cap reached")));

        await Assert.ThrowsAsync<LeaseUnavailableException>(
            () => client.AcquireAsync(TimeSpan.FromSeconds(60), EmptyMetadata, CancellationToken.None));
    }

    [Fact]
    public async Task AcquireAsync_WhenTypedLeaseUnavailable_Propagates()
    {
        // The current SDK throws LeaseUnavailableException directly on a 409 acquire; it must pass through.
        DiagnosticsProfilerLeaseClient client = new FakeAcquireLeaseClient(
            () => Task.FromException<Guid>(new LeaseUnavailableException("cap reached")));

        await Assert.ThrowsAsync<LeaseUnavailableException>(
            () => client.AcquireAsync(TimeSpan.FromSeconds(60), EmptyMetadata, CancellationToken.None));
    }

    [Fact]
    public async Task AcquireAsync_WhenNon409Error_IsNotTranslated()
    {
        // A non-cap error must remain a generic RequestFailedException so the caller fails open.
        DiagnosticsProfilerLeaseClient client = new FakeAcquireLeaseClient(
            () => Task.FromException<Guid>(new RequestFailedException((int)System.Net.HttpStatusCode.InternalServerError, "server down")));

        await Assert.ThrowsAsync<RequestFailedException>(
            () => client.AcquireAsync(TimeSpan.FromSeconds(60), EmptyMetadata, CancellationToken.None));
    }

    [Fact]
    public async Task AcquireAsync_WhenGranted_ReturnsLeaseId()
    {
        Guid expected = Guid.NewGuid();
        DiagnosticsProfilerLeaseClient client = new FakeAcquireLeaseClient(() => Task.FromResult(expected));

        Guid actual = await client.AcquireAsync(TimeSpan.FromSeconds(60), EmptyMetadata, CancellationToken.None);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_WhenSeamReturns409_ReturnsNull()
    {
        // End-to-end through the real lease seam: a cap-reached 409 on acquire must skip the cycle
        // (return null), not fail open. Guards against the acquire path silently no-op'ing the cap.
        DiagnosticsProfilerLeaseClient leaseClient = new FakeAcquireLeaseClient(
            () => Task.FromException<Guid>(new RequestFailedException((int)System.Net.HttpStatusCode.Conflict, "cap reached")));

        ProfilerConcurrencyControlClient controlClient = CreateConcurrencyControlClient(leaseClient);

        IAsyncDisposable? result = await controlClient.TryAcquireLeaseAsync(CancellationToken.None);

        Assert.Null(result);
    }

    private static ProfilerConcurrencyControlClient CreateConcurrencyControlClient(IProfilerLeaseClient leaseClient)
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
}
