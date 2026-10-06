using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceProfiler.Common.Utilities;

namespace Microsoft.ApplicationInsights.Profiler.Core.TraceControls
{
    internal sealed class DiagnosticsClientTraceControl : ITraceControl, IDisposable
    {
        private readonly string _typeName;
        private static SemaphoreSlim _singleTraceSessionHandle = new SemaphoreSlim(1, 1);
        private readonly DiagnosticsClientProvider _diagnosticsClientProvider;
        private readonly DiagnosticsClientTraceConfiguration _configuration;
        private readonly IThreadUtilities _threadUtilities;
        private readonly UserConfiguration _userConfiguration;
        private readonly ILogger _logger;
        private EventPipeSession? _currentSession;
        private Task<bool>? _traceFileWritingTask;
        private const string TimeoutMessage = "Timed out waiting for semaphore.";

        // How long disposal waits for the session to stop and the writer to drain. Short, because
        // disposal runs on the shutdown path.
        private static readonly TimeSpan DisposeDrainTimeout = TimeSpan.FromSeconds(5);

        // Teardown state for the current session. Scoped per session rather than per instance: this
        // control is a singleton reused for every scheduled session, so a latched instance flag would
        // permanently suppress the unexpected-teardown diagnostics for every later session.
        private SessionTeardown? _teardown;

        public DiagnosticsClientTraceControl(
            DiagnosticsClientProvider diagnosticsClientProvider,
            DiagnosticsClientTraceConfiguration configuration,
            IThreadUtilities threadUtilities,
            IOptions<UserConfiguration> userConfiguration,
            ILogger<DiagnosticsClientTraceControl> logger
            )
        {
            _typeName = this.GetType().Name;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _userConfiguration = userConfiguration?.Value ?? throw new ArgumentNullException(nameof(userConfiguration));
            _diagnosticsClientProvider = diagnosticsClientProvider ?? throw new ArgumentNullException(nameof(diagnosticsClientProvider));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _threadUtilities = threadUtilities ?? throw new ArgumentNullException(nameof(threadUtilities));
        }

        public DateTime? SessionStartUTC { get; private set; }

        public async Task<bool> DisableAsync(CancellationToken cancellationToken)
        {
            _logger.LogTrace("[{typeName}] Entering {methodName}()...", _typeName, nameof(DisableAsync));

            try
            {
                _teardown?.Request();
                // Snapshot the field: a concurrent Dispose() can null it while this stop is in flight.
                Task<bool>? writingTask = _traceFileWritingTask;
                await StopProfilerSessionAsync(disposeEventSessionImmediately: false, cancellationToken).ConfigureAwait(false);

                if (writingTask is not null)
                {
                    // The writer reports whether the copy actually finished. It swallows a closed
                    // stream when the diagnostic endpoint is gone, so completing is not the same as
                    // having written a usable trace.
                    return await writingTask.ConfigureAwait(false);
                }
                else
                {
                    _logger.LogError("Trace file writing task is null upon disabling tracing. This should not happen.");
                    return false;
                }
            }
            finally
            {
                DisposeEventPipeSession();
            }
        }

        public async Task EnableAsync(string traceFilePath, CancellationToken cancellationToken)
        {
            _logger.LogTrace("Entering {typeName}.{methodName}()", _typeName, nameof(EnableAsync));
            if (await _singleTraceSessionHandle.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    if (_currentSession != null)
                    {
                        throw new InvalidOperationException("Only 1 session at a time is supported.");
                    }

                    SessionStartUTC = DateTime.UtcNow;

                    int pid = default;
                    try
                    {
                        pid = CurrentProcessUtilities.GetId();
                    }
                    catch (InvalidOperationException ex) when (!_userConfiguration.AllowsCrash)
                    {
                        _logger.LogError(ex, "Failed getting process id. Profiler won't start.");
                        return;
                    }

                    _currentSession = _diagnosticsClientProvider.GetDiagnosticsClient().StartEventPipeSession(
                        _configuration.BuildEventPipeProviders(),
                        requestRundown: _configuration.RequestRundown,
                        circularBufferMB: _configuration.CircularBufferMB);

                    _logger.LogTrace("Triggering writing to trace file");
                    SessionTeardown teardown = new();
                    _teardown = teardown;
                    _traceFileWritingTask = StartWriteAsync(traceFilePath, _currentSession.EventStream, teardown);
                    _logger.LogTrace("{name} enabled.", _typeName);
                }
                finally
                {
                    _singleTraceSessionHandle.Release();
                }
                return;
            }
            else
            {
                throw new TimeoutException(TimeoutMessage);
            }
        }

        private async Task StopProfilerSessionAsync(bool disposeEventSessionImmediately, CancellationToken cancellationToken)
        {
            if (_singleTraceSessionHandle.Wait(TimeSpan.FromSeconds(10), cancellationToken))
            {
                try
                {
                    if (_currentSession != null)
                    {
                        _logger.LogTrace("Trigger stopping an EventPipe session . . .");
                        TimeSpan timeout = TimeSpan.FromHours(1);
                        try
                        {
                            await _threadUtilities.CallWithTimeoutAsync(_currentSession.Stop, timeout).ConfigureAwait(false);
                        }
                        catch (TimeoutException ex)
                        {
                            _logger.LogInformation(ex, "Can't stop EventPipe in given period: {timeout}. Profiler is failing. Please restart your service. This might caused by a known bug. Refer to https://aka.ms/ep-sp/bugs/135 for more details.", timeout);
                        }
                        catch (ServerNotAvailableException ex)
                        {
                            _logger.LogInformation(ex, "If the application is shutting down, it is safe to ignore it. Refer to https://aka.ms/ep-sp/bugs/117 for more details.");
                        }

                        _logger.LogTrace("The eventpipe session is stopped. ");

                        if (disposeEventSessionImmediately)
                        {
                            DisposeEventPipeSession();
                        }
                    }

                    _logger.LogTrace("[{typeName}] Disabled.", _typeName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected exception stopping session.");
                    throw;
                }
                finally
                {
                    _singleTraceSessionHandle.Release();
                }
            }
            else
            {
                throw new TimeoutException(TimeoutMessage);
            }
        }

        private async Task<bool> StartWriteAsync(string traceFilePath, Stream readFrom, SessionTeardown teardown, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogTrace("Starts to write to file: {fileName}", traceFilePath);
                using Stream writeTo = new FileStream(traceFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                _logger.LogTrace("Start writing file ...");
                await readFrom.CopyToAsync(writeTo, bufferSize: 81920, cancellationToken: cancellationToken).ConfigureAwait(false);
                _logger.LogTrace("Finish writing file.");
                return true;
            }
            catch (ObjectDisposedException ex)
            {
                if (teardown.IsRequested)
                {
                    // We closed the stream ourselves while stopping or disposing the session. The
                    // trace is incomplete, but this is a lifecycle outcome rather than an
                    // application fault, so it must not be reported as an error.
                    _logger.LogWarning(ex, "The EventPipe stream was closed while the profiler was stopping. The trace file is incomplete and will not be processed.");
                    return false;
                }

                _ = CurrentProcessUtilities.TryGetId(out int? pid);

                string? eventPipeIPCFullPath = null;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    eventPipeIPCFullPath = FormattableString.Invariant($@"\\.pipe\dotnet-diagnostic-{pid}");
                }
                else
                {
                    try
                    {
                        // Try best match on other platforms
                        string ipcRootPath = Path.GetTempPath();
                        eventPipeIPCFullPath = Directory.EnumerateFiles(ipcRootPath, $"dotnet-diagnostic-{pid}-*-socket", SearchOption.TopDirectoryOnly)
                            .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                            .FirstOrDefault();
                    }
                    catch (InvalidOperationException)
                    {
                        _logger.LogDebug("No EventPipe pipeline file.");
                    }
                }

                if (string.IsNullOrEmpty(eventPipeIPCFullPath) || !File.Exists(eventPipeIPCFullPath))
                {
                    // The IPC file doesn't exist, there isn't too much to be done. Log a warning for scenario analysis.
                    // The copy did not finish, so the trace file is incomplete and must not be uploaded.
                    _logger.LogWarning(ex, "Profiler service is closed. This happens when application is shutting down. The trace file is incomplete and will not be processed.");
                    return false;
                }
                else
                {
                    // If the IPC file exists, it is unexpected ObjectDisposedException, rethrow for logging errors.
                    throw;
                }
            }
            // No hanging upon exception
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write the trace file.");
                throw;
            }
        }

        private void DisposeEventPipeSession()
        {
            _logger.LogTrace("[{typeName}] Disposing eventpipe session.", _typeName);
            _currentSession?.Dispose();
            _currentSession = null;
            // Clear the writer and its teardown state alongside the session, so a later DisableAsync
            // cannot re-await the previous session's completed task or observe its teardown flag.
            _traceFileWritingTask = null;
            _teardown = null;
            _logger.LogTrace("[{typeName}] Eventpipe session disposed.", _typeName);
        }

        public void Dispose()
        {
            // Disposing closes the EventPipe stream. Coordinate with the writer first, exactly as
            // DisableAsync does, so disposal cannot truncate the trace and surface a closed pipe as
            // an application error (issue #191).
            _teardown?.Request();

            // Take the session handle so teardown cannot interleave with an in-flight stop. Bounded:
            // if a stop already owns the session it will do the coordinated teardown itself, and
            // blocking shutdown on it indefinitely would be worse than disposing underneath it.
            bool acquired = false;
            try
            {
                acquired = _singleTraceSessionHandle.Wait(DisposeDrainTimeout);
            }
            catch (ObjectDisposedException)
            {
            }

            try
            {
                EventPipeSession? session = _currentSession;
                if (session is not null)
                {
                    // Stop so the stream can reach EOF; without it the drain below could never
                    // finish. Bounded and on a dedicated thread: this is a blocking IPC call on the
                    // shutdown path, where the thread pool may be saturated.
                    RunBounded(session.Stop, DisposeDrainTimeout, "stop the EventPipe session");

                    _traceFileWritingTask?.Wait(DisposeDrainTimeout);
                }

                DisposeEventPipeSession();
            }
            finally
            {
                if (acquired)
                {
                    _singleTraceSessionHandle.Release();
                }
            }
        }

        /// <summary>
        /// Runs a blocking operation on a dedicated background thread and waits for it for at most
        /// <paramref name="timeout"/>. A dedicated thread is used because the thread pool can be
        /// saturated during shutdown, which would leave a queued operation unstarted.
        /// </summary>
        private void RunBounded(Action operation, TimeSpan timeout, string description)
        {
            Thread thread = new(() =>
            {
                try
                {
                    operation();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to {description} during teardown.", description);
                }
            })
            {
                IsBackground = true,
                Name = "EventPipe teardown",
            };

            thread.Start();
            if (!thread.Join(timeout))
            {
                _logger.LogDebug("Timed out trying to {description} during teardown.", description);
            }
        }

        /// <summary>
        /// Teardown state for a single profiling session. Held per session so that an abandoned
        /// writer keeps its own state and a later session cannot observe or reset it.
        /// </summary>
        private sealed class SessionTeardown
        {
            private volatile bool _requested;

            public bool IsRequested => _requested;

            public void Request() => _requested = true;
        }
    }
}
