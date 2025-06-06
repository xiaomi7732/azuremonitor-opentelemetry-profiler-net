// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using Azure.Core;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Uploader;
using Xunit;

using static ServiceProfiler.EventPipe.Upload.Tests.UploadContextExtensionTestHelper;

namespace ServiceProfiler.EventPipe.Upload.Tests
{
    public class ProfilerFrontendClientBuilderTests
    {
        [Fact]
        public void ShouldRequireCallWithUploadContext()
        {
            ProfilerFrontendClientBuilder target = new ProfilerFrontendClientBuilder();
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => target.Build());
            Assert.StartsWith(
                $"Call {nameof(ProfilerFrontendClientBuilder.WithUploadContext)} before calling {nameof(ProfilerFrontendClientBuilder.Build)}.",
                exception.Message);
        }

        [Fact]
        public void ShouldHaveTokenCredential()
        {
            UploadContext uploadContext = CreateUploadContext();
            UploadContextExtension uploadContextExtension = new UploadContextExtension(uploadContext);
            uploadContextExtension.AccessToken = new AccessToken("token", DateTimeOffset.UtcNow.AddMinutes(5));

            ProfilerFrontendClientBuilder target = new ProfilerFrontendClientBuilder();
            target.WithUploadContext(uploadContextExtension);
            TokenCredential result = target.GetTokenCredential();

            // Access token is provided through the UploadContextExtension.
            Assert.NotNull(result);
        }

        [Fact]
        public void ShouldHaveNoTokenCredential()
        {
            UploadContext uploadContext = CreateUploadContext();
            UploadContextExtension uploadContextExtension = new UploadContextExtension(uploadContext);
            uploadContextExtension.AccessToken = null;

            ProfilerFrontendClientBuilder target = new ProfilerFrontendClientBuilder();
            target.WithUploadContext(uploadContextExtension);
            TokenCredential result = target.GetTokenCredential();

            // token credential shall be null when there's no access token provided.
            Assert.Null(result);
        }

        [Fact]
        public void ShouldKeepChainByCallingWithUploadContext()
        {
            UploadContext uploadContext = CreateUploadContext();
            UploadContextExtension uploadContextExtension = new UploadContextExtension(uploadContext);
            uploadContextExtension.AccessToken = new AccessToken("token", DateTimeOffset.UtcNow.AddMinutes(5));

            ProfilerFrontendClientBuilder target = new ProfilerFrontendClientBuilder();
            IProfilerFrontendClientBuilder result = target.WithUploadContext(uploadContextExtension);

            Assert.NotNull(result);
            Assert.Same(target, result);
        }
    }
}
