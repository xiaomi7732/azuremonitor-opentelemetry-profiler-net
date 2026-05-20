// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Azure.Monitor.Diagnostics.Profiler;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Utilities;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Uploader;

internal class ProfilerClientFactory : IProfilerClientFactory
{
    private readonly ILogger<ProfilerClientFactory> _logger;

    public ProfilerClientFactory(ILogger<ProfilerClientFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IProfilerClient Create(UploadContextExtension uploadContextExtension)
    {
        if (uploadContextExtension is null)
        {
            throw new ArgumentNullException(nameof(uploadContextExtension));
        }

        UploadContext context = uploadContextExtension.UploadContext;

        string? agentString = uploadContextExtension.AdditionalData?.AgentString;
        if (string.IsNullOrEmpty(agentString))
        {
            agentString = FormattableString.Invariant($"EventPipeUploader/{EnvironmentUtilities.ExecutingAssemblyInformationalVersion}");
            _logger.LogWarning("AgentString was not provided via IPCAdditionalData. Falling back to default: {agentString}. Consider setting it via IAgentStringProvider.", agentString);
        }

        ProfilerClientOptions options = new()
        {
            Endpoint = context.HostUrl,
            InstrumentationKey = context.AIInstrumentationKey.ToString("D"),
            MachineName = EnvironmentUtilities.MachineName,
            UserAgent = agentString,
        };

        return new ProfilerClient(options, uploadContextExtension.TokenCredential);
    }
}
