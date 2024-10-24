using Azure.Core;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.Auth;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.IPC;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Contract;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal class PostStopProcessor : IPostStopProcessor
{
    private readonly ServiceProfilerOptions _serviceProfilerOptions;
    private readonly IUploaderPathProvider _uploaderPathProvider;
    private readonly ITraceUploader _traceUploader;
    private readonly INamedPipeClientFactory _namedPipeClientFactory;
    private readonly IAuthTokenProvider _authTokenProvider;
    private readonly ISerializationProvider _serializer;
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly IMetadataWriter _metadataWriter;
    private readonly ICustomEventsTracker _customEventsTracker;
    private readonly IRoleNameSource _roleNameSource;
    private readonly ILogger _logger;

    public PostStopProcessor(
        IUploaderPathProvider uploaderPathProvider,
        ITraceUploader traceUploader,
        IOptions<ServiceProfilerOptions> serviceProfilerOptions,
        INamedPipeClientFactory namedPipeClientFactory,
        IAuthTokenProvider authTokenProvider,
        ISerializationProvider serializer,
        IServiceProfilerContext serviceProfilerContext,
        IMetadataWriter metadataWriter,
        ICustomEventsTracker customEventsTracker,
        IRoleNameSource roleNameSource,
        ILogger<PostStopProcessor> logger)
    {
        _serviceProfilerOptions = serviceProfilerOptions?.Value ?? throw new ArgumentNullException(nameof(serviceProfilerOptions));
        _uploaderPathProvider = uploaderPathProvider ?? throw new ArgumentNullException(nameof(uploaderPathProvider));
        _traceUploader = traceUploader ?? throw new ArgumentNullException(nameof(traceUploader));
        _namedPipeClientFactory = namedPipeClientFactory ?? throw new ArgumentNullException(nameof(namedPipeClientFactory));
        _authTokenProvider = authTokenProvider ?? throw new ArgumentNullException(nameof(authTokenProvider));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _metadataWriter = metadataWriter ?? throw new ArgumentNullException(nameof(metadataWriter));
        _customEventsTracker = customEventsTracker ?? throw new ArgumentNullException(nameof(customEventsTracker));
        _roleNameSource = roleNameSource ?? throw new ArgumentNullException(nameof(roleNameSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PostStopProcessAsync(PostStopOptions e, CancellationToken cancellationToken)
    {
        // TODO: Remove
        await Task.Yield();
        // ~

        _logger.LogTrace("Entering {name}", nameof(PostStopProcessAsync));

        try
        {
            int sampleCount = e.Samples.Count();

            _logger.LogDebug("There are {sampleNumber} samples before validation.", sampleCount);
            UploadMode uploadMode = _serviceProfilerOptions.UploadMode;

            // Execute the upload
            if ((sampleCount > 0 || uploadMode == UploadMode.Always) && _uploaderPathProvider.TryGetUploaderFullPath(out string uploaderFullPath))
            {
                e.UploaderFullPath = uploaderFullPath;

                int processId = CurrentProcessUtilities.GetId();

                string pipeName = Guid.NewGuid().ToString("D");
                Guid appId = Guid.Empty;

                Task namedPipeClientTask = Task.Run(async () =>
                {
                    INamedPipeClientService namedPipeClient = _namedPipeClientFactory.CreateNamedPipeService();
                    try
                    {
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
                        double longerTimeoutMilliseconds = Math.Max(TimeSpan.FromMinutes(10).TotalMilliseconds, _serviceProfilerOptions.NamedPipe.DefaultMessageTimeout.TotalMilliseconds);
                        e.Samples = (await namedPipeClient.ReadAsync<IEnumerable<SampleActivity>>(timeout: TimeSpan.FromMilliseconds(longerTimeoutMilliseconds)).ConfigureAwait(false))
                            ?? Enumerable.Empty<SampleActivity>();
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
                    }
                    finally
                    {
                        (namedPipeClient as IDisposable)?.Dispose();
                    }
                }, cancellationToken);

                Task<UploadContextModel?> uploadTask = UploadTraceAsync(e, processId, pipeName, cancellationToken);

                // Waiting for both task to finish.
                await Task.WhenAll(namedPipeClientTask, uploadTask).ConfigureAwait(false);

                UploadContextModel? uploadContext = uploadTask.Result;
                if (uploadContext != null)
                {
                    // Trace is uploaded.
                    int validSampleCount = e.Samples.Count();
                    _logger.LogDebug("Sending {validSampleCount} valid custom events to AI. Valid sample count equals total sample count: {result}", validSampleCount, validSampleCount == sampleCount);

                    _customEventsTracker.Send(e.Samples, uploadContext, processId, e.ProfilerSource, appId);
                }
            }

            // Delete trace unless set to preserve.
            _logger.LogDebug("{preserveTraceFile} is set to {value}.", nameof(_serviceProfilerOptions.PreserveTraceFile), _serviceProfilerOptions.PreserveTraceFile);
            if (!_serviceProfilerOptions.PreserveTraceFile)
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
            _logger.LogTrace(ex, message: ex.ToString());
            throw;
        }
    }

    /// <summary>
    /// Upload the trace file.
    /// </summary>
    /// <returns>Returns the upload context when upload succeeded. Returns null otherwise.</returns>
    private async Task<UploadContextModel?> UploadTraceAsync(
        PostStopOptions options, int processId, string namedPipeName, CancellationToken cancellationToken)
    {
        bool areOptionsSerialized = _serializer.TrySerialize(options, out string? serializedOptions);
        _logger.LogTrace(@"Trace session finished. Invoking upload. Args:
{arguments}", serializedOptions);

        string metadataPath = Path.ChangeExtension(options.TraceFilePath, "metadata");
        string machineName = _serviceProfilerContext.MachineName;
        await CreateMetadataAsync(options.Samples.Select(sample =>
            // At this point, we don't have a stamp id or the datacube yet, just use 'localstamp' as the stamp id.
            // Since the metadata file stays in the .etl.zip, it is not needed to locate the trace file.
            sample.ToArtifactLocationProperties("localstamp", processId, options.SessionId, Guid.Empty, machineName)
        ), metadataPath, cancellationToken).ConfigureAwait(false);

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

    private Task CreateMetadataAsync(IEnumerable<ArtifactLocationProperties> locations, string targetPath, CancellationToken cancellationToken)
        => _metadataWriter.WriteAsync(targetPath, locations, cancellationToken);

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
}