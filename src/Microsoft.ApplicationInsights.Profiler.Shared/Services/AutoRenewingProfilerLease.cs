// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Azure.Monitor.Diagnostics;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

/// <summary>
/// Holds a profiler concurrency lease alive by periodically renewing it, and releases it
/// when disposed. Renewal failures are swallowed (logged) so they never disrupt the
/// in-flight profiling session; the lease will simply expire server-side.
/// </summary>
internal sealed class AutoRenewingProfilerLease : IAsyncDisposable
{
    private readonly IProfilerLeaseClient _leaseClient;
    private readonly Guid _leaseId;
    private readonly TimeSpan _renewInterval;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _renewTask;
    private readonly Stopwatch _heldStopwatch = Stopwatch.StartNew();

    public AutoRenewingProfilerLease(
        IProfilerLeaseClient leaseClient,
        Guid leaseId,
        TimeSpan renewInterval,
        ILogger logger)
    {
        _leaseClient = leaseClient ?? throw new ArgumentNullException(nameof(leaseClient));
        _leaseId = leaseId;
        _renewInterval = renewInterval;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _renewTask = Task.Run(RenewLoopAsync);
    }

    private async Task RenewLoopAsync()
    {
        CancellationToken cancellationToken = _cancellationTokenSource.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_renewInterval, cancellationToken).ConfigureAwait(false);
                try
                {
                    await _leaseClient.RenewAsync(_leaseId, cancellationToken).ConfigureAwait(false);
                    _logger.LogTrace("Renewed profiler concurrency lease {LeaseId}.", _leaseId);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Disposing; stop renewing.
                    throw;
                }
                catch (LeaseUnavailableException ex)
                {
                    _logger.LogDebug(
                        "Profiler concurrency lease {LeaseId} lost or expired during renewal; stopping renewal. " +
                        "The current session will finish; the lease will expire server-side. {Reason}",
                        _leaseId, ex.Message);
                    _logger.LogTrace(ex, "Lease renewal failure detail for {LeaseId}.", _leaseId);
                    return;
                }
#pragma warning disable CA1031 // Fail-open: a transient renew error must not disrupt profiling.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    // Transient error: log and keep trying on the next interval.
                    _logger.LogDebug("Transient error renewing profiler concurrency lease {LeaseId}: {Reason}", _leaseId, ex.Message);
                    _logger.LogTrace(ex, "Lease renewal error detail for {LeaseId}.", _leaseId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cancellationTokenSource.Cancel();
        try
        {
            await _renewTask.ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Best-effort cleanup; never throw from dispose.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogTrace(ex, "Profiler concurrency lease renewal task ended with an error for {LeaseId}.", _leaseId);
        }

        _cancellationTokenSource.Dispose();

        try
        {
            await _leaseClient.ReleaseAsync(_leaseId, CancellationToken.None).ConfigureAwait(false);
            _logger.LogDebug("Released profiler concurrency lease {LeaseId} after {HeldMs}ms.", _leaseId, _heldStopwatch.ElapsedMilliseconds);
        }
#pragma warning disable CA1031 // Release failures are non-fatal; the lease expires server-side.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogDebug("Failed to release profiler concurrency lease {LeaseId}; it will expire server-side. {Reason}", _leaseId, ex.Message);
            _logger.LogTrace(ex, "Lease release error detail for {LeaseId}.", _leaseId);
        }
    }
}
