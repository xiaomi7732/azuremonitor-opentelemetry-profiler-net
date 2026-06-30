// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

/// <summary>
/// Thin seam over the raw backend lease operations used for profiler concurrency
/// control. The default implementation wraps the public Azure Monitor diagnostics lease
/// API; abstracting it keeps the higher-level concurrency logic unit-testable.
/// </summary>
internal interface IProfilerLeaseClient
{
    /// <summary>
    /// Acquires a lease for the configured instrumentation key and profiler lease namespace.
    /// </summary>
    /// <param name="duration">The requested lease duration (15-60 seconds).</param>
    /// <param name="metadata">Diagnostic metadata for the request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The acquired lease ID.</returns>
    /// <exception cref="Azure.Monitor.Diagnostics.LeaseUnavailableException">
    /// The concurrency cap has been reached.
    /// </exception>
    Task<Guid> AcquireAsync(TimeSpan duration, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken);

    /// <summary>
    /// Renews a previously acquired lease.
    /// </summary>
    Task RenewAsync(Guid leaseId, CancellationToken cancellationToken);

    /// <summary>
    /// Releases a previously acquired lease.
    /// </summary>
    Task ReleaseAsync(Guid leaseId, CancellationToken cancellationToken);
}
