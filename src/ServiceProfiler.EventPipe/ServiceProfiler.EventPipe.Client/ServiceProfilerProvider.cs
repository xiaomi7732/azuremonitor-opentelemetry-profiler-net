//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Azure.Core;
using Microsoft.ApplicationInsights.Profiler.Core.Auth;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Core.EventListeners;
using Microsoft.ApplicationInsights.Profiler.Core.IPC;
using Microsoft.ApplicationInsights.Profiler.Core.Logging;
using Microsoft.ApplicationInsights.Profiler.Core.Orchestration;
using Microsoft.ApplicationInsights.Profiler.Core.SampleTransfers;
using Microsoft.ApplicationInsights.Profiler.Core.TraceControls;
using Microsoft.ApplicationInsights.Profiler.Core.UploaderProxy;
using Microsoft.ApplicationInsights.Profiler.Core.Utilities;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Contract;
using Microsoft.ServiceProfiler.Orchestration;
using Microsoft.ServiceProfiler.Utilities;
using ServiceProfiler.Common.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Core;

internal sealed class ServiceProfilerProvider : IServiceProfilerProvider, IDisposable
{
    public ServiceProfilerProvider(
        IServiceProfilerContext serviceProfilerContext,
        ITraceControl traceControl,
        ITraceSessionListenerFactory traceSessionListenerFactory,
        IEventPipeTelemetryTracker telemetryTracker,
        ICustomEventsTracker customEventsTracker,
        IRoleNameSource roleNameSource,
        ITraceUploader traceUploader,
        IMetadataWriter metadataWriter,
        IOptions<UserConfiguration> serviceProfilerConfiguration,
        IAppInsightsSinks appInsightsSinks,
        INamedPipeClientFactory namedPipeClientFactory,
        IAuthTokenProvider aADAuthTokenServiceFactory,
        IUploaderPathProvider uploaderPathProvider,
        ISerializationProvider serializer,
        IUserCacheManager userCacheManager,
        ILogger<ServiceProfilerProvider> logger)
    {
        // Required
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _userCacheManager = userCacheManager ?? throw new ArgumentNullException(nameof(userCacheManager));
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _terminateTokenSource = _serviceProfilerContext.ServiceProfilerCancellationTokenSource;

        _customEventsTracker = customEventsTracker ?? throw new ArgumentNullException(nameof(customEventsTracker));
        _roleNameSource = roleNameSource ?? throw new ArgumentNullException(nameof(roleNameSource));
        _traceUploader = traceUploader ?? throw new ArgumentNullException(nameof(traceUploader));
        _metadataWriter = metadataWriter ?? throw new ArgumentNullException(nameof(metadataWriter));
        _serviceProfilerConfiguration = serviceProfilerConfiguration.Value ?? throw new ArgumentNullException(nameof(serviceProfilerConfiguration));

        _traceControl = traceControl ?? throw new ArgumentNullException(nameof(traceControl));
        _traceSessionListenerFactory = traceSessionListenerFactory ?? throw new ArgumentNullException(nameof(traceSessionListenerFactory));
        _telemetryTracker = telemetryTracker ?? throw new ArgumentNullException(nameof(telemetryTracker));
        _appInsightsSinks = appInsightsSinks ?? throw new ArgumentNullException(nameof(appInsightsSinks));
        _namedPipeClientFactory = namedPipeClientFactory ?? throw new ArgumentNullException(nameof(namedPipeClientFactory));
        _authTokenProvider = aADAuthTokenServiceFactory ?? throw new ArgumentNullException(nameof(aADAuthTokenServiceFactory));
        _uploaderPathProvider = uploaderPathProvider ?? throw new ArgumentNullException(nameof(uploaderPathProvider));
    }

    /// <summary>
    /// Starts the Service Profiler using the configured scheduler.
    /// </summary>
    public async Task<bool> StartServiceProfilerAsync(IProfilerSource source, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Entering {StartServiceProfilerAsync}.", nameof(StartServiceProfilerAsync));

        try
        {
            if (!await _singleProfilingSemaphore.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false))
            {
                _logger.LogDebug("No semaphore fetched within given time to check profiling state. There is another profiling in progress. Give it up.");
                return false;
            }
        }
        catch (Exception ex) when (ex is ObjectDisposedException || ex is OperationCanceledException)
        {
            _logger.LogDebug("Semaphore disposed or cancelled. Profiler won't start.");
            return false;
        }

        _logger.LogDebug("Got the semaphore. Try starting the Profiler.");
        bool profilerStarted = false;
        try
        {
            // Got the semaphore within given time. Try to start Profiler.
            _appInsightsSinks.LogInformation(StartProfilerTriggered);

            string localCacheFolder = _userCacheManager.TempTraceDirectory.FullName;
            Directory.CreateDirectory(localCacheFolder);
            _currentTraceFilePath = Path.ChangeExtension(Path.Combine(localCacheFolder, Guid.NewGuid().ToString()), TraceFileExtension);
            _logger.LogDebug("Trace File Path: {traceFilePath}", _currentTraceFilePath);

            try
            {
                _logger.LogDebug("Call TraceControl.Enable().");
                _traceControl.Enable(_currentTraceFilePath);
                profilerStarted = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start eventpipe profiling.");
                throw;
            }

            _appInsightsSinks.LogInformation(StartProfilerSucceeded);

            Guid authenticatedUserId = await _serviceProfilerContext.GetAppInsightsAppIdAsync().ConfigureAwait(false);
            _telemetryTracker.SetCustomerAppInfo(authenticatedUserId);

            _logger.LogDebug("Start to create trace session listener by its factory");
            _sessionListeners = _traceSessionListenerFactory.CreateTraceSessionListeners().GetEnumerator();
            ActivateNext(this, null);

            _logger.LogDebug("TraceSessionListener created.");
            _logger.LogInformation(TelemetryConstants.SessionStarted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Starting profiler failed.");
            throw;
        }
        finally
        {
            // Return the resource if profiler is not started.
            if (!profilerStarted)
            {
                _singleProfilingSemaphore.Release();
            }
        }

        return profilerStarted;
    }

    private void ActivateNext(object sender, EventArgs e)
    {
        if (SessionListener != null)
        {
            SessionListener.Poisoned -= ActivateNext;
            SessionListener.Dispose();
        }

        if (_sessionListeners.MoveNext())
        {
            SessionListener = _sessionListeners.Current;
            SessionListener.Poisoned += ActivateNext;
            _logger.LogDebug("Activating trace session listener: {traceSessionListener}", SessionListener.GetType().Name);
            SessionListener.Activate();
        }
        else
        {
            _logger.LogInformation("All session listeners failed to have valid samples. Profiling Session failed.");
        }
    }

    /// <summary>
    /// Stops the current Service Profiler.
    /// </summary>
    public async Task<bool> StopServiceProfilerAsync(IProfilerSource source, CancellationToken cancellationToken)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        bool profilerStopped = false;

        _appInsightsSinks.LogInformation(StopProfilerTriggered);
        _logger.LogDebug("Entering {stopServiceProfilerAsync}.", nameof(StopServiceProfilerAsync));

        // Sanity check
        if (!IsProfiling)
        {
            _logger.LogDebug("Try to stop profiler while none is is running.");
        }
        else
        {
            try
            {
                // Notice: Stop trace session listener has to be happen before calling ITraceControl.Disable().
                // Trace control disabling will take a while. We shall not gathering anything events when disable is happening.
                _logger.LogDebug("Disabling {sessionListener}", nameof(SessionListener));
                List<SampleActivity> sampleActivities = SessionListener?.SampleActivities?.GetActivities()?.ToList();
                SessionListener?.Dispose();
                SessionListener = null;

                try
                {
                    await _traceControl.DisableAsync(cancellationToken).ConfigureAwait(false);
                    profilerStopped = true;
                    // The critical resource of EventPipe profiling has already been disabled successfully here.
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fail to disable EventPipe profiling.");
                    throw;
                }

                await PostStopProcessAsync(
                    new PostStopOptions(
                        traceFilePath: _currentTraceFilePath,
                        sessionId: _traceControl.SessionStartUTC,
                        stampFrontendHostUrl: _serviceProfilerContext.StampFrontendEndpointUrl,
                        samples: sampleActivities,
                        profilerSource: source),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                _appInsightsSinks.LogInformation(StopProfilerSucceeded);

                _logger.LogInformation(TelemetryConstants.SessionEnded);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error happens on stopping service profiler tracing.");
                throw;
            }
            finally
            {
                try
                {
                    if (profilerStopped && _singleProfilingSemaphore?.CurrentCount == 0)
                    {
                        _singleProfilingSemaphore?.Release();
                    }
                }
                catch (SemaphoreFullException ex)
                {
                    _logger.LogWarning(ex, "Additional releasing of semaphore upon stopping profiler. This should not happen very often. Please contact the project owner otherwise.");
                }
            }
        }

        return profilerStopped;
    }

    /// <summary>
    /// Event handler when a service profiler session ends.
    /// </summary>
    private async Task PostStopProcessAsync(PostStopOptions e, CancellationToken cancellationToken)
    {
        _logger.LogTrace("Entering {postStopProcessAsync}", nameof(PostStopProcessAsync));
        try
        {
            int sampleCount = e.Samples?.Count() ?? 0;

            _logger.LogDebug("There are {sampleNumber} samples before validation.", sampleCount);
            UploadMode uploadMode = _serviceProfilerConfiguration.UploadMode;
            // Execute the upload
            if ((sampleCount > 0 || uploadMode == UploadMode.Always) && _uploaderPathProvider.TryGetUploaderFullPath(out string uploaderFullPath))
            {
                e.UploaderFullPath = uploaderFullPath;

                int processId = CurrentProcessUtilities.GetId();

                string pipeName = Guid.NewGuid().ToString("D");
                Guid appId = Guid.Empty;
                Task namedPipeClientTask = Task.Run(async () =>
                {
                    using INamedPipeClientService namedPipeClient = _namedPipeClientFactory.CreateNamedPipeService();

                    _logger.LogTrace("Waiting for connection of named pipe: {name}", pipeName);
                    await namedPipeClient.ConnectAsync(pipeName, cancellationToken).ConfigureAwait(false);
                    _logger.LogTrace("Namedpipe {name} connected.", namedPipeClient.PipeName);

                    _logger.LogTrace("Sending serialized samples.");
                    await namedPipeClient.SendAsync(e.Samples).ConfigureAwait(false);
                    _logger.LogTrace("Finished sending samples.");

                    // Contract with Uploader: Only valid samples are written back.
                    _logger.LogTrace("Waiting for the uploader to write back valid samples according to the contract.");
                    // The uploader might need a while for sample validation before it returns the result. That is especially true under heavy loaded system.
                    // Give it at least 10 minutes as a reasonable timeout. The user could choose to overwrite it with even longer timespan by set up operation timeout.
                    double longerTimeoutMilliseconds = Math.Max(TimeSpan.FromMinutes(10).TotalMilliseconds, _serviceProfilerConfiguration.NamedPipe.DefaultMessageTimeout.TotalMilliseconds);
                    e.Samples = await namedPipeClient.ReadAsync<IEnumerable<SampleActivity>>(timeout: TimeSpan.FromMilliseconds(longerTimeoutMilliseconds)).ConfigureAwait(false);
                    _logger.LogTrace("Finished loading valid samples.");

                    // Sending the AccessToken for AAD authentication in case it is enabled.
                    _logger.LogTrace("Sending access token");
                    AccessToken accessToken = await _authTokenProvider.GetTokenAsync(cancellationToken: default).ConfigureAwait(false);
                    await namedPipeClient.SendAsync(accessToken).ConfigureAwait(false);
                    _logger.LogTrace("Finished sending access token for the uploader to use.");

                    // Contract with Uploader: Return app id.
                    _logger.LogTrace("Waiting for the uploader to write back valid appId as dataCube.");
                    appId = await namedPipeClient.ReadAsync<Guid>().ConfigureAwait(false);
                    _logger.LogTrace("Finished retrieving a valid appId (dataCube): {appId}", appId);
                    if (appId == Guid.Empty)
                    {
                        throw new InvalidOperationException($"Datacube {appId} is invalid.");
                    }
                }, cancellationToken);
                Task<UploadContext> uploadTask = UploadTraceAsync(e, processId, pipeName, cancellationToken);

                // Waiting for both task to finish.
                await Task.WhenAll(namedPipeClientTask, uploadTask).ConfigureAwait(false);

                UploadContext uploadContext = uploadTask.Result;
                if (uploadContext != null)
                {
                    // Trace is uploaded.
                    int validSampleCount = e.Samples?.Count() ?? 0;
                    _logger.LogDebug("Sending {validSampleCount} valid custom events to AI. Valid sample count equals total sample count: {result}", validSampleCount, validSampleCount == sampleCount);

                    _customEventsTracker.Send(e.Samples, uploadContext, processId, e.ProfilerSource, appId);
                }
            }

            // Delete trace unless set to preserve.
            _logger.LogDebug("{preserveTraceFile} is set to {value}.", nameof(_serviceProfilerConfiguration.PreserveTraceFile), _serviceProfilerConfiguration.PreserveTraceFile);
            if (!_serviceProfilerConfiguration.PreserveTraceFile)
            {
                TryDeleteFiles(e.TraceFilePath);
            }
            else
            {
                _logger.LogInformation("Trace is preserved locally at: {tracePath}", e.TraceFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error happens on stopping service profiler.");
            _logger.LogTrace(ex.ToString());
            throw;
        }
    }

    private void TryDeleteFiles(params string[] filePaths)
    {
        if (filePaths == null || filePaths.Length == 0)
        {
            return;
        }

        foreach (string filePath in filePaths)
        {
            try
            {
                File.Delete(filePath);

            }
            catch (Exception ex)
                when (ex is ArgumentException ||
                    ex is ArgumentNullException ||
                    ex is DirectoryNotFoundException ||
                    ex is IOException)
            {
                _logger.LogDebug(ex, "Fail to delete file at: {path}", filePath);
            }
        }
    }

    /// <summary>
    /// Upload the trace file.
    /// </summary>
    /// <returns>Returns the upload context when upload succeeded. Returns null otherwise.</returns>
    private async Task<UploadContext> UploadTraceAsync(
        PostStopOptions options, int processId, string namedPipeName, CancellationToken cancellationToken)
    {
        bool areOptionsSerialized = _serializer.TrySerialize(options, out string serializedOptions);
        _logger.LogTrace(@"Trace session finished. Invoking upload. Args:
{arguments}", serializedOptions);

        string metadataPath = Path.ChangeExtension(options.TraceFilePath, "metadata");
        string machineName = EnvironmentUtilities.MachineName;
        CreateMetadata(options.Samples.NullAsEmpty().Select(sample =>
            // At this point, we don't have a stamp id or the datacube yet, just use 'localstamp' as the stamp id.
            // Since the metadata file stays in the .etl.zip, it is not needed to locate the trace file.
            sample.ToArtifactLocationProperties("localstamp", processId, options.SessionId, Guid.Empty, machineName)
        ), metadataPath);

        return await _traceUploader.UploadAsync(
            options.SessionId,
            options.TraceFilePath,
            metadataPath,
            sampleFilePath: null,// Replaced by namedpipe name.
            namedPipeName,
            _roleNameSource.CloudRoleName,
            options.ProfilerSource,
            cancellationToken,
            options.UploaderFullPath).ConfigureAwait(false);
    }

    private void CreateMetadata(IEnumerable<ArtifactLocationProperties> locations, string targetPath)
    {
        _metadataWriter.Write(targetPath, locations);
    }

    internal const string TraceFileExtension = ".netperf";

    private SemaphoreSlim _singleProfilingSemaphore = new SemaphoreSlim(1, 1);
    private bool IsProfiling => _singleProfilingSemaphore.CurrentCount == 0;

    private readonly ILogger _logger;
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly ICustomEventsTracker _customEventsTracker;
    private readonly IRoleNameSource _roleNameSource;
    private readonly ITraceUploader _traceUploader;
    private readonly IMetadataWriter _metadataWriter;
    private readonly UserConfiguration _serviceProfilerConfiguration;
    private readonly ITraceControl _traceControl;
    private readonly IEventPipeTelemetryTracker _telemetryTracker;

    private IEnumerator<ITraceSessionListener> _sessionListeners;
    internal ITraceSessionListener SessionListener { get; private set; }

    // Having ServiceProfiler context to dispose this.
    private readonly CancellationTokenSource _terminateTokenSource;
    private readonly ITraceSessionListenerFactory _traceSessionListenerFactory;

    private readonly IAppInsightsSinks _appInsightsSinks;
    private readonly INamedPipeClientFactory _namedPipeClientFactory;
    private readonly IAuthTokenProvider _authTokenProvider;
    private readonly IUploaderPathProvider _uploaderPathProvider;
    private readonly ISerializationProvider _serializer;
    private readonly IUserCacheManager _userCacheManager;
    private string _currentTraceFilePath;

    private const string StartProfilerTriggered = "StartProfiler triggered.";
    private const string StartProfilerSucceeded = "StartProfiler succeeded.";
    private const string StopProfilerTriggered = "StopProfiler triggered.";
    private const string StopProfilerSucceeded = "StopProfiler succeeded.";

    public void Dispose()
    {
        _singleProfilingSemaphore?.Dispose();
        _singleProfilingSemaphore = null;

        _sessionListeners?.Dispose();
        _sessionListeners = null;
    }
}
