using System;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ServiceProfiler.Agent.FrontendClient;
using Microsoft.ServiceProfiler.Utilities;

using static System.FormattableString;

namespace Microsoft.ApplicationInsights.Profiler.Uploader
{
    internal class ProfilerFrontendClientBuilder : IProfilerFrontendClientBuilder
    {
        private UploadContextExtension? _uploadContextExtension;

        public IProfilerFrontendClient Build()
        {
            if (_uploadContextExtension is null)
            {
                throw new InvalidOperationException(Invariant($"Call {nameof(WithUploadContext)} before calling {nameof(Build)}."));
            }

            string agentString = Invariant($"EventPipeUploader/{EnvironmentUtilities.ExecutingAssemblyInformationalVersion}");
            if (!string.IsNullOrEmpty(_uploadContextExtension.AdditionalData?.AgentString))
            {
                agentString = _uploadContextExtension.AdditionalData.AgentString;
            }

            UploadContext context = _uploadContextExtension.UploadContext;

            return new ProfilerFrontendClient(
                host: context.HostUrl,
                instrumentationKey: context.AIInstrumentationKey,
                machineName: EnvironmentUtilities.MachineName,
                featureVersion: null,
                userAgent: agentString,
                tokenCredential: _uploadContextExtension.TokenCredential,
                skipCertificateValidation: context.SkipEndpointCertificateValidation);
        }

        public IProfilerFrontendClientBuilder WithUploadContext(UploadContextExtension uploadContext)
        {
            _uploadContextExtension = uploadContext ?? throw new ArgumentNullException(nameof(uploadContext));
            return this;
        }
    }
}
