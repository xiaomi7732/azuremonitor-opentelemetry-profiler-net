#nullable enable

using System;
using System.Reflection.Metadata;
using Azure.Core;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;

#if EP_OTEL_PROFILER
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
#endif

namespace Microsoft.ApplicationInsights.Profiler.Uploader
{
    internal class UploadContextExtension
    {
        public UploadContextExtension(UploadContext uploadContext)
        {
            UploadContext = uploadContext ?? throw new ArgumentNullException(nameof(uploadContext));
        }

        public UploadContext UploadContext { get; }
        public Guid VerifiedAppId { get; set; }

#if EP_OTEL_PROFILER
        [Obsolete($"Use {nameof(TokenCredential)} instead.", error: true)]
#endif
        public AccessToken? AccessToken { get; set; }


#if EP_OTEL_PROFILER
        public IPCAdditionalData? AdditionalData { get; set; }

        public TokenCredential? TokenCredential { get; set; } = null;
#else
        private TokenCredential? _tokenCredential;
        public TokenCredential? TokenCredential
        {
            get
            {
                if (_tokenCredential != null)
                {
                    return _tokenCredential;
                }

                return AccessToken is null || string.IsNullOrEmpty(AccessToken.Value.Token)
                    ? null
                    : new StaticAccessTokenCredential(new AccessToken(AccessToken.Value.Token, AccessToken.Value.ExpiresOn));
            }
            set
            {
                _tokenCredential = value;
            }
        }
#endif
    }
}
