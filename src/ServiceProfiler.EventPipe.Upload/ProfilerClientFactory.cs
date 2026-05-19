// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Azure.Monitor.Diagnostics.Profiler;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ServiceProfiler.Utilities;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Uploader;

internal class ProfilerClientFactory : IProfilerClientFactory
{
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
