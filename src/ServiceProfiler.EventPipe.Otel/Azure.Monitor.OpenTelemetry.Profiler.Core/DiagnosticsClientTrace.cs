//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal sealed class DiagnosticsClientTrace : ITraceControl, IDisposable
{
    public DateTime? SessionStartUTC { get; private set; }
    private EventPipeSession? _session;
    private readonly DiagnosticsClientProvider _clientProvider;
    private readonly DiagnosticsClientTraceConfiguration _configuration;
    private readonly ITargetProcess _targetProcess;
    private readonly ILogger<DiagnosticsClientTrace> _logger;

    private int _targetProcessId;

    public DiagnosticsClientTrace(
        DiagnosticsClientProvider clientProvider,
        DiagnosticsClientTraceConfiguration configuration,
        ITargetProcess targetProcess,
        ILogger<DiagnosticsClientTrace> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _clientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _targetProcess = targetProcess ?? throw new ArgumentNullException(nameof(targetProcess));
    }

    /// <inheritdoc />
    public async Task<int> DisableAsync(CancellationToken cancellationToken = default)
    {
        if (_session is null)
        {
            _logger.LogWarning("{name} is called when the session doesn't exist.", nameof(DisableAsync));
            return 0;
        }
        await _session.StopAsync(cancellationToken).ConfigureAwait(false);
        return _targetProcessId;
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }

    /// <summary>
    /// Enables a profiler session.
    /// </summary>
    /// <param name="traceFilePath">The trace file path. Default to 'default.nettrace'.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns></returns>
    public async Task EnableAsync(string traceFilePath = $"default{OpenTelemetryProfilerProvider.TraceFileExtension}" /* ==> default.nettrace*/, CancellationToken cancellationToken = default)
    {
        SessionStartUTC = DateTime.UtcNow;
        _targetProcessId = _targetProcess.ProcessId;

        _session = await _clientProvider.GetDiagnosticsClient(_targetProcessId).StartEventPipeSessionAsync(
            providers: _configuration.BuildEventPipeProviders(),
            requestRundown: _configuration.RequestRundown,
            circularBufferMB: _configuration.CircularBufferMB,
            token: cancellationToken).ConfigureAwait(false);

        SessionStarted(traceFilePath, _session.EventStream);
    }

    // Fire and forget
    private async void SessionStarted(string traceFilePath, Stream stream)
    {
        _logger.LogInformation("Start writing trace file {traceFilePath}...", traceFilePath);
        try
        {
            using FileStream fileStream = File.OpenWrite(traceFilePath);
            await stream.CopyToAsync(fileStream).ConfigureAwait(false);
            _logger.LogInformation("Finished writing trace file {traceFilePath}.", traceFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing trace file at: {filePath}", traceFilePath);
        }
    }
}