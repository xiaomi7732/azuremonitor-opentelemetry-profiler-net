using System;
using Azure.Core;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ServiceProfiler.Agent.FrontendClient;
using Microsoft.ServiceProfiler.Utilities;

using static System.FormattableString;

namespace Microsoft.ApplicationInsights.Profiler.Uploader
{
    internal class ProfilerFrontendClientBuilder : IProfilerFrontendClientBuilder
    {
        private UploadContextExtension _uploadContextExtension;

        public IProfilerFrontendClient Build()
        {
            if (_uploadContextExtension == null)
            {
                throw new InvalidOperationException(Invariant($"Call {nameof(WithUploadContext)} before calling {nameof(Build)}."));
            }

            UploadContext context = _uploadContextExtension.UploadContext;
            return new ProfilerFrontendClient(
                host: context.HostUrl,
                instrumentationKey: context.AIInstrumentationKey,
                machineName: EnvironmentUtilities.MachineName,
                featureVersion: null,
                userAgent: Invariant($"EventPipeUploader/{EnvironmentUtilities.ExecutingAssemblyInformationalVersion}"),
                tokenCredential: _uploadContextExtension.TokenCredential,
                skipCertificateValidation: context.SkipEndpointCertificateValidation);
        }

        public IProfilerFrontendClientBuilder WithUploadContext(UploadContextExtension uploadContext)
        {
            _uploadContextExtension = uploadContext ?? throw new ArgumentNullException(nameof(uploadContext));
            return this;
        }

#if EP_OTEL_PROFILER
        [Obsolete("Stop using this. Use the credential provided by the agent directly.", error: true)]
#endif
        internal TokenCredential GetTokenCredential() =>
            (_uploadContextExtension.AccessToken.HasValue) ?
        new StaticAccessTokenCredential(_uploadContextExtension.AccessToken.Value) :
        null;
    }
}
