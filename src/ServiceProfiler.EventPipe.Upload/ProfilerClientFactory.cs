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

        string agentString = uploadContextExtension.AdditionalData?.AgentString
            ?? throw new InvalidOperationException("AgentString must be provided via IPCAdditionalData. The calling agent should set it using IAgentStringProvider.");

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
