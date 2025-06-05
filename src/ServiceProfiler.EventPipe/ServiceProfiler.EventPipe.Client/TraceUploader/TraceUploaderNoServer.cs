//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.ApplicationInsights.Profiler.Core.UploaderProxy
{
    /// <summary>
    /// Uploader proxy when no service profiler frontend used.
    /// </summary>
    internal sealed class TraceUploaderNoServer : ITraceUploader
    {
        private readonly ILogger<ITraceUploader> _logger;
        private readonly IServiceProfilerContext _context;
        private readonly UserConfiguration _userConfiguration;

        public TraceUploaderNoServer(
            IServiceProfilerContext context,
            IOptions<UserConfiguration> userConfiguration,
            ILogger<ITraceUploader> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _userConfiguration = userConfiguration.Value ?? throw new ArgumentNullException(nameof(userConfiguration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<UploadContext> UploadAsync(
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
            if (string.IsNullOrEmpty(traceFilePath) && string.IsNullOrEmpty(namedPipeName))
            {
                throw new InvalidOperationException($"'{nameof(sampleFilePath)}' and '{nameof(namedPipeName)}' cannot be null or empty at the same time");
            }

            if (string.IsNullOrEmpty(metadataFilePath))
            {
                throw new ArgumentException($"'{nameof(metadataFilePath)}' cannot be null or empty", nameof(metadataFilePath));
            }

            if (string.IsNullOrEmpty(sampleFilePath))
            {
                throw new ArgumentException($"'{nameof(sampleFilePath)}' cannot be null or empty", nameof(sampleFilePath));
            }

            _logger.LogInformation("Uploader is called in standalone mode. Upload didn't actually happen.");
            return Task.FromResult(new UploadContext()
            {
                AIInstrumentationKey = _context.AppInsightsInstrumentationKey,
                HostUrl = _context.StampFrontendEndpointUrl,
                StampId = "NoServiceProfilerStamp",
                SessionId = sessionId,
                TraceFilePath = traceFilePath,
                MetadataFilePath = metadataFilePath,
                PreserveTraceFile = _userConfiguration.PreserveTraceFile,
                SkipEndpointCertificateValidation = _userConfiguration.SkipEndpointCertificateValidation,
                UploadMode = _userConfiguration.UploadMode,
                SerializedSampleFilePath = sampleFilePath,
                RoleName = roleName,
                TriggerType = triggerType,
            });
        }
    }
}
