// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Azure.Monitor.Diagnostics;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

/// <summary>
/// Acquires server-enforced concurrency leases so that not all instances of the same
/// application profile at once. Fail-open: only an explicit cap-reached response prevents
/// profiling; any other failure allows profiling to proceed.
/// </summary>
internal sealed class ProfilerConcurrencyControlClient : IProfilerConcurrencyControlClient
{
    /// <summary>
    /// Initial lease duration. The backend accepts values between 15 and 60 seconds.
    /// </summary>
    internal static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How often to renew the lease while a profiling session runs.
    /// </summary>
    internal static readonly TimeSpan RenewInterval = TimeSpan.FromSeconds(5);

    private readonly IProfilerLeaseClient _leaseClient;
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly IRoleNameSource _roleNameSource;
    private readonly IRoleInstanceSource _roleInstanceSource;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    public ProfilerConcurrencyControlClient(
        IProfilerLeaseClient leaseClient,
        IServiceProfilerContext serviceProfilerContext,
        IRoleNameSource roleNameSource,
        IRoleInstanceSource roleInstanceSource,
        ILoggerFactory loggerFactory,
        ILogger<ProfilerConcurrencyControlClient> logger)
    {
        _leaseClient = leaseClient ?? throw new ArgumentNullException(nameof(leaseClient));
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _roleNameSource = roleNameSource ?? throw new ArgumentNullException(nameof(roleNameSource));
        _roleInstanceSource = roleInstanceSource ?? throw new ArgumentNullException(nameof(roleInstanceSource));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IAsyncDisposable?> TryAcquireLeaseAsync(CancellationToken cancellationToken)
    {
        string roleName = _roleNameSource.CloudRoleName;
        string roleInstance = _roleInstanceSource.CloudRoleInstance;

        IReadOnlyDictionary<string, string> metadata = new Dictionary<string, string>
        {
            ["RoleName"] = roleName,
            ["RoleInstance"] = roleInstance,
        };

        _logger.LogTrace(
            "Acquiring profiler concurrency lease for {RoleName}/{RoleInstance} (requested duration {DurationSeconds}s). Metadata: {@Metadata}",
            roleName, roleInstance, (int)LeaseDuration.TotalSeconds, metadata);

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            Guid leaseId = await _leaseClient.AcquireAsync(LeaseDuration, metadata, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(
                "Acquired profiler concurrency lease {LeaseId} for {RoleName}/{RoleInstance} in {ElapsedMs}ms.",
                leaseId, roleName, roleInstance, stopwatch.ElapsedMilliseconds);

            return new AutoRenewingProfilerLease(
                _leaseClient, leaseId, RenewInterval,
                _loggerFactory.CreateLogger<AutoRenewingProfilerLease>());
        }
        catch (LeaseUnavailableException)
        {
            _logger.LogInformation(
                "Profiler concurrency lease unavailable - fleet concurrency cap reached for {RoleName}/{RoleInstance}. Skipping this profiling cycle.",
                roleName, roleInstance);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Fail-open: never block profiling because of an infrastructure error.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(
                "Failed to acquire profiler concurrency lease ({Reason}); proceeding with profiling anyway (fail-open). Endpoint {Endpoint}.",
                ex.Message, _serviceProfilerContext.StampFrontendEndpointUrl.Host);
            _logger.LogTrace(ex, "Lease acquisition error detail for {RoleName}/{RoleInstance}.", roleName, roleInstance);
            return NoOpProfilerConcurrencyControlClient.GrantedLease;
        }
    }
}
