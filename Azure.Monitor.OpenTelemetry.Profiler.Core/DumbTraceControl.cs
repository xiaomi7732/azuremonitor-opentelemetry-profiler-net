
using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal sealed class DumbTraceControl : ITraceControl, IDisposable
{
    public DateTime? SessionStartUTC { get; private set; }
    private EventPipeSession? _session;
    private readonly DiagnosticsClientProvider _clientProvider;
    private readonly ILogger<DumbTraceControl> _logger;


    public DumbTraceControl(DiagnosticsClientProvider clientProvider, ILogger<DumbTraceControl> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _clientProvider = clientProvider ?? throw new ArgumentNullException(nameof(clientProvider));
    }

    public async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        if (_session is not null)
        {
            await _session.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        // TODO: Throw when session doesn't exist?
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }

    public async Task EnableAsync(string traceFilePath = "default.nettrace", CancellationToken cancellationToken = default)
    {
        SessionStartUTC = DateTime.UtcNow;

        using Process currentProcess = Process.GetCurrentProcess();
        int pid = currentProcess.Id;

        _session = await _clientProvider.GetDiagnosticsClient(pid).StartEventPipeSessionAsync(CreateConfigurations(), cancellationToken).ConfigureAwait(false);

        _ = StartWriting(traceFilePath, _session.EventStream);
    }

    // Fire and forget
    private async Task StartWriting(string traceFilePath, Stream stream)
    {
        _logger.LogInformation("Start writing trace file {traceFilePath}...", traceFilePath);
        try
        {
            using FileStream fileStream = File.OpenWrite(traceFilePath);
            await stream.CopyToAsync(fileStream).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error writing trace file at: {filePath}", traceFilePath);
        }
        finally
        {
            _logger.LogInformation("Finished writing trace file {traceFilePath}.", traceFilePath);
        }
    }

    private EventPipeSessionConfiguration CreateConfigurations()
    {
        return new EventPipeSessionConfiguration([
             // Provider Name: Microsoft-Windows-DotNETRuntime("e13c0d23-ccbc-4e12-931b-d9cc2eee27e4")
                // This is the CLR provider.  The most important of these are the GC events, JIT compilation events.
                // (the JIT events are needed to decode the stack addresses).
                new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, keywords: 0x4c14fccbd, arguments: null),

                // Private provider.
                // Provider Name: Microsoft-Windows-DotNETRuntimePrivate("763fd754-7086-4dfe-95eb-c01a46faf4ca")
                // This is the 'Private' CLR provider. We would like to get rid of this, and mostly only has some less important GC events
                new EventPipeProvider("Microsoft-Windows-DotNETRuntimePrivate", EventLevel.Verbose, keywords: 0x4002000b, arguments: null),

                // Sample profiler.
                // Profiler Name: Microsoft-DotNETCore-SampleProfiler("3c530d44-97ae-513a-1e6d-783e8f8e03a9")
                // This is the provider that generates a CPU stack every msec.
                new EventPipeProvider("Microsoft-DotNETCore-SampleProfiler", EventLevel.Verbose, keywords: 0x0, arguments: null),

                // TPL.
                // Provider Name: System.Threading.Tasks.TplEventSource("2e5dba47-a3d2-4d16-8ee0-6671ffdcd7b5")
                // This is the provider that generates ‘Task Parallel Library’ events.
                // These events are needed to stitch together stacks from different threads (when async I/O happens).
                new EventPipeProvider("System.Threading.Tasks.TplEventSource", EventLevel.Verbose, keywords: 0x1 | 0x2 | 0x4 | 0x40 | 0x80, arguments: null),

                // Microsoft-ApplicationInsights-DataRelay
                // new EventPipeProvider(ApplicationInsightsDataRelayEventSource.EventSourceName, EventLevel.Verbose, keywords:0xffffffff, arguments: null),
                
                // Open Telemetry SDK Event Source
                new EventPipeProvider("OpenTelemetry-Sdk",EventLevel.Verbose, keywords:0xfffffffff, arguments: null),
        ]);
    }
}