// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Monitor.Diagnostics;
using Azure.Monitor.Diagnostics.Models;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.Auth;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Auth;
using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

/// <summary>
/// Default <see cref="IProfilerLeaseClient"/> that wraps the public
/// <see cref="DiagnosticsClient"/> lease API. The underlying client is created lazily so
/// the instrumentation key / endpoint are resolved at first use.
/// </summary>
internal class DiagnosticsProfilerLeaseClient : IProfilerLeaseClient
{
    private const string LeaseNamespace = LeaseNamespaces.Profiler;

    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly IAuthTokenProvider _authTokenProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Lazy<DiagnosticsClient> _diagnosticsClient;
    private readonly Lazy<string> _iKey;

    public DiagnosticsProfilerLeaseClient(
        IServiceProfilerContext serviceProfilerContext,
        IAuthTokenProvider authTokenProvider,
        ILoggerFactory loggerFactory)
    {
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _authTokenProvider = authTokenProvider ?? throw new ArgumentNullException(nameof(authTokenProvider));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _diagnosticsClient = new Lazy<DiagnosticsClient>(CreateDiagnosticsClient);
        _iKey = new Lazy<string>(() => _serviceProfilerContext.AppInsightsInstrumentationKey.ToString("D"));
    }

    private DiagnosticsClient CreateDiagnosticsClient()
    {
        TokenCredential? credential = _authTokenProvider.IsAADAuthenticateEnabled
            ? new AADAuthTokenCredential(_authTokenProvider, _loggerFactory.CreateLogger<AADAuthTokenCredential>())
            : null;

        DiagnosticsClientOptions options = new()
        {
            Endpoint = _serviceProfilerContext.StampFrontendEndpointUrl,
        };

        return new DiagnosticsClient(options, credential);
    }

    public Task<Guid> AcquireAsync(TimeSpan duration, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken)
        => TranslateLeaseLostAsync(AcquireCoreAsync(duration, metadata, cancellationToken));

    public Task RenewAsync(Guid leaseId, CancellationToken cancellationToken)
        => TranslateLeaseLostAsync(RenewCoreAsync(leaseId, cancellationToken));

    public Task ReleaseAsync(Guid leaseId, CancellationToken cancellationToken)
        => TranslateLeaseLostAsync(ReleaseCoreAsync(leaseId, cancellationToken));

    // Raw calls into the underlying SDK, isolated as an overridable seam so the 409 -> typed-exception
    // translation can be unit-tested without a live backend.
    internal virtual Task<Guid> AcquireCoreAsync(TimeSpan duration, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken)
        => _diagnosticsClient.Value.AcquireLeaseAsync(_iKey.Value, LeaseNamespace, duration, metadata, cancellationToken);

    internal virtual Task RenewCoreAsync(Guid leaseId, CancellationToken cancellationToken)
        => _diagnosticsClient.Value.RenewLeaseAsync(_iKey.Value, LeaseNamespace, leaseId, cancellationToken);

    internal virtual Task ReleaseCoreAsync(Guid leaseId, CancellationToken cancellationToken)
        => _diagnosticsClient.Value.ReleaseLeaseAsync(_iKey.Value, LeaseNamespace, leaseId, cancellationToken);

    /// <summary>
    /// Translates a cap-reached / lost-lease response (HTTP 409) surfaced as a generic
    /// <see cref="RequestFailedException"/> into a typed <see cref="LeaseUnavailableException"/> so callers
    /// can distinguish "cap reached / lease lost" from transient errors.
    /// The underlying client already throws <see cref="LeaseUnavailableException"/> directly for a 409 on
    /// acquire, but surfaces it as a generic <see cref="RequestFailedException"/> on renew/release; acquire
    /// is routed through here too so a single, symmetric contract holds regardless of that asymmetry (and is
    /// a no-op when the SDK already throws the typed exception).
    /// </summary>
    private static async Task<T> TranslateLeaseLostAsync<T>(Task<T> operation)
    {
        try
        {
            return await operation.ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)System.Net.HttpStatusCode.Conflict)
        {
            throw new LeaseUnavailableException("The lease is unavailable.", ex);
        }
    }

    private static async Task TranslateLeaseLostAsync(Task operation)
    {
        try
        {
            await operation.ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)System.Net.HttpStatusCode.Conflict)
        {
            throw new LeaseUnavailableException("The lease is unavailable.", ex);
        }
    }
}
