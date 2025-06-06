//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Core.Logging;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Agent.Exceptions;
using Microsoft.ServiceProfiler.Agent.FrontendClient;

namespace Microsoft.ApplicationInsights.Profiler.Core.UploaderProxy;

internal class TraceUploaderProxy : ITraceUploader
{
    private readonly ILogger _logger;
    private readonly IServiceProfilerContext _context;
    private readonly IFile _fileService;
    private readonly IOutOfProcCallerFactory _uploaderCallerFactory;
    private readonly IUploadContextValidator _uploadContextValidator;
    private readonly IProfilerCoreAssemblyInfo _profilerVersion;
    private readonly UserConfiguration _userConfiguration;
    private readonly IUploaderPathProvider _uploaderPathProvider;
    private readonly IProfilerFrontendClient _profilerFrontendClient;

    public TraceUploaderProxy(
        IUploaderPathProvider uploaderPathProvider,
        IProfilerFrontendClient profilerFrontendClient,
        IFile fileService,
        IOutOfProcCallerFactory uploaderCallerFactory,
        IServiceProfilerContext context,
        ILogger<TraceUploaderProxy> logger,
        IOptions<UserConfiguration> userConfiguration,
        IUploadContextValidator uploadContextValidator,
        IProfilerCoreAssemblyInfo profilerVersion)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _uploaderPathProvider = uploaderPathProvider ?? throw new ArgumentNullException(nameof(uploaderPathProvider));
        _profilerFrontendClient = profilerFrontendClient ?? throw new ArgumentNullException(nameof(profilerFrontendClient));
        _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
        _uploaderCallerFactory = uploaderCallerFactory ?? throw new ArgumentNullException(nameof(uploaderCallerFactory));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _userConfiguration = userConfiguration.Value ?? throw new ArgumentNullException(nameof(userConfiguration));
        _uploadContextValidator = uploadContextValidator ?? throw new ArgumentNullException(nameof(uploadContextValidator));
        _profilerVersion = profilerVersion ?? throw new ArgumentNullException(nameof(profilerVersion));
    }

    public async Task<UploadContext> UploadAsync(
        DateTimeOffset sessionId,
        string traceFilePath,
        string metadataFilePath,
        string sampleFilePath,
        string namedPipeName,
        string roleName,
        string triggerType,
        CancellationToken cancellationToken,
        string uploaderFullPath = null)
    {
        if (string.IsNullOrEmpty(traceFilePath))
        {
            _logger.LogError("Trace file path is not specified.");
            return null;
        }

        if (!_fileService.Exists(traceFilePath))
        {
            _logger.LogError("Trace file {0} can't be found.", traceFilePath);
            return null;
        }

        if (string.IsNullOrEmpty(sampleFilePath) && string.IsNullOrEmpty(namedPipeName))
        {
            throw new InvalidOperationException($"'{nameof(sampleFilePath)}' and '{nameof(namedPipeName)}' cannot be null or empty at the same time");
        }

        if (_userConfiguration.UploadMode == UploadMode.Never)
        {
            _logger.LogInformation("Skip upload according to user configuration.");
            return null;
        }

        // TODO: Defer the fetch of StampId to the uploader will simplify this a lot. Let the uploader return the stamp id. It also address the issue that
        // 2 getting stamp ids might return different values.

        // Stamp Id should be fetched successfully.
        string stampId = null;
        string stampIdFetchFailureMessage = "Could not get the stamp id. Aborting the upload process.";
        try
        {
            stampId = await _profilerFrontendClient.GetStampIdAsync(_context.ServiceProfilerCancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (InstrumentationKeyInvalidException)
        {
            stampId = null;
            stampIdFetchFailureMessage = $"{stampIdFetchFailureMessage} Please make sure the instrumentation key is valid.";
        }
        catch (HttpRequestException requestException) when (requestException.Message.Contains("401 (Unauthorized)."))
        {
            stampId = null;
            stampIdFetchFailureMessage = $"{stampIdFetchFailureMessage} Please make sure the instrumentation key is valid.";
        }

        if (string.IsNullOrEmpty(stampId))
        {
            _logger.LogError(stampIdFetchFailureMessage);
            return null;
        }
        // Locate uploader.
        uploaderFullPath ??= _uploaderPathProvider.GetUploaderFullPath();

        if (string.IsNullOrEmpty(uploaderFullPath))
        {
            _logger.LogError("Trace Uploader is not provided or located.");
            return null;
        }

        _logger.LogInformation("Uploader to be used: {uploaderPath}", uploaderFullPath);

        // Upload is ready to go.
        UploadContext uploadContext = new UploadContext()
        {
            AIInstrumentationKey = _context.AppInsightsInstrumentationKey,
            HostUrl = _context.StampFrontendEndpointUrl,
            StampId = stampId,
            SessionId = sessionId,
            TraceFilePath = traceFilePath,
            MetadataFilePath = metadataFilePath,
            PreserveTraceFile = _userConfiguration.PreserveTraceFile,
            SkipEndpointCertificateValidation = _userConfiguration.SkipEndpointCertificateValidation,
            UploadMode = _userConfiguration.UploadMode,
            SerializedSampleFilePath = sampleFilePath,
            PipeName = namedPipeName,
            RoleName = roleName,
            TriggerType = triggerType,
            Environment = _userConfiguration.UploaderEnvironment,
        };

        // Validation Failed
        string message = _uploadContextValidator.Validate(uploadContext);
        if (!string.IsNullOrEmpty(message))
        {
            _logger.LogError(message);
            return null;
        }

        int exitCode = await CallUploadAsync(uploaderFullPath, uploadContext.ToString(), cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            // Upload Failed
            _logger.LogError("Trace upload failed. Exit code: {exitCode}", exitCode);
            return null;
        }

        // Upload succeeded.
        return uploadContext;
    }

    private Task<int> CallUploadAsync(string exePath, string args, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            const string executableName = "dotnet";
            try
            {
                // Assuming dotnet core SDK will always be installed.
                _uploaderCallerFactory.Setup(executableName, exePath + ' ' + args);

                int exitCode = _uploaderCallerFactory.ExecuteAndWait(ProcessPriorityClass.BelowNormal);

                _logger.LogInformation("{0} Exit code: {1}", TelemetryConstants.CallTraceUploaderFinished, exitCode);
                return exitCode;
            }
            catch (Exception ex) when (
                ex is InvalidOperationException ||
                ex is Win32Exception ||
                ex is ObjectDisposedException ||
                ex is PlatformNotSupportedException)
            {
                _logger.LogError(ex, "Failed to start uploader process. Make sure the application can access the executable of {executable} by having its directory on PATH environment variable. Check the message for details.", executableName);
                return int.MinValue;
            }
        }, cancellationToken);
    }
}
