// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

/// <summary>
/// A no-op concurrency control client that always grants. Used when there is no backend
/// to lease from (for example, standalone mode).
/// </summary>
internal sealed class NoOpProfilerConcurrencyControlClient : IProfilerConcurrencyControlClient
{
    /// <summary>
    /// A shared no-op lease handle. Disposing it does nothing.
    /// </summary>
    internal static readonly IAsyncDisposable GrantedLease = new NoOpLease();

    public Task<IAsyncDisposable?> TryAcquireLeaseAsync(CancellationToken cancellationToken)
        => Task.FromResult<IAsyncDisposable?>(GrantedLease);

    private sealed class NoOpLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => default;
    }
}
