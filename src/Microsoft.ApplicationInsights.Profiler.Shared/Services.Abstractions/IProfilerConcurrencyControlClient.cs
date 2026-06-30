// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

/// <summary>
/// Coordinates profiling concurrency across many instances of the same application by
/// acquiring a shared, server-enforced lease before a profiling session starts.
/// </summary>
/// <remarks>
/// The backend enforces the maximum number of instances that may profile concurrently.
/// Implementations are expected to be <b>fail-open</b>: only an explicit "cap reached"
/// response from the service should prevent profiling. Any other failure (service down,
/// network error, missing endpoint, timeout) must allow profiling to proceed.
/// </remarks>
internal interface IProfilerConcurrencyControlClient
{
    /// <summary>
    /// Attempts to acquire a concurrency lease for a profiling session.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// A non-null <see cref="IAsyncDisposable"/> lease handle when profiling may proceed
    /// (either the lease was acquired, or acquisition failed-open). Dispose the handle
    /// when the profiling session ends to release the lease. Returns <see langword="null"/>
    /// only when the service explicitly denied the lease because the concurrency cap has
    /// been reached; in that case the caller should skip this profiling cycle.
    /// </returns>
    Task<IAsyncDisposable?> TryAcquireLeaseAsync(CancellationToken cancellationToken);
}
