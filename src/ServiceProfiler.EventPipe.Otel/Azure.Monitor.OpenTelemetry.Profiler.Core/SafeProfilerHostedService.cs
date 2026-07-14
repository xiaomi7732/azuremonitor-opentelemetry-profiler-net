//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

/// <summary>
/// A fail-safe wrapper around a profiler <see cref="IHostedService"/>. The profiler is a diagnostic add-on
/// and must never take down the application it profiles, so this wrapper guarantees that a failure while
/// starting, running, or stopping the profiler is caught and logged - it never propagates to the host.
///
/// Crucially, the inner hosted service is resolved <b>lazily inside <see cref="StartAsync"/></b> rather than
/// during dependency-injection resolution. Some profiler services throw while being constructed (for
/// example an invalid connection string, a backend client that cannot be created, or - in the codeless
/// site-extension scenario - a dependency version the profiler was compiled against but the application
/// does not provide). Resolving lazily keeps that failure inside the try/catch here, so host startup is not
/// faulted.
/// </summary>
internal sealed class SafeProfilerHostedService : IHostedService
{
    private readonly Func<IHostedService?> _innerFactory;
    private readonly ILogger _logger;
    private IHostedService? _inner;

    public SafeProfilerHostedService(Func<IHostedService?> innerFactory, ILogger<SafeProfilerHostedService> logger)
    {
        _innerFactory = innerFactory ?? throw new ArgumentNullException(nameof(innerFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _inner = _innerFactory();
            if (_inner is null)
            {
                return;
            }

            await _inner.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Disable this part of the profiler; the application is unaffected.
            _inner = null;
            _logger.LogError(ex, "Azure Monitor Profiler failed to start and has been disabled. The application is unaffected.");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_inner is null)
        {
            return;
        }

        try
        {
            await _inner.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure Monitor Profiler failed to stop cleanly. The application is unaffected.");
        }
    }
}
