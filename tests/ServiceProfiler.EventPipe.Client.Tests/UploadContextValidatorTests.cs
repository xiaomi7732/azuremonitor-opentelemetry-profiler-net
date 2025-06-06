using System;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Core.Utilities;
using Xunit;

namespace ServiceProfiler.EventPipe.Client.Tests
{
    public class UploadContextValidatorTests
    {
        [Fact]
        public void ShouldReturnNullWhenNoError()
        {
            IUploadContextValidator target = new UploadContextValidator(fileExists: p => true);
            string actual = target.Validate(new UploadContext()
            {
                AIInstrumentationKey = Guid.NewGuid(),
                HostUrl = new Uri("https://endpoint", UriKind.Absolute),
                SessionId = DateTimeOffset.UtcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = @"c:\metadataFilePath.metadata",
                PreserveTraceFile = false,
                SkipEndpointCertificateValidation = false,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
            });

            // Return null upon success.
            Assert.Null(actual);
        }

        [Fact]
        public void ShouldAcceptOptionalMetadataFilePath()
        {
            IUploadContextValidator target = new UploadContextValidator(fileExists: p => true);
            string actual = target.Validate(new UploadContext()
            {
                AIInstrumentationKey = Guid.NewGuid(),
                HostUrl = new Uri("https://endpoint"),
                SessionId = DateTimeOffset.UtcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = null,
                PreserveTraceFile = false,
                SkipEndpointCertificateValidation = false,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
            });

            // Return null upon success.
            Assert.Null(actual);
        }

        [Fact]
        public void ShouldAcceptOptionalRoleName()
        {
            IUploadContextValidator target = new UploadContextValidator(fileExists: p => true);
            string actual = target.Validate(new UploadContext()
            {
                AIInstrumentationKey = Guid.NewGuid(),
                HostUrl = new Uri("https://endpoint"),
                SessionId = DateTimeOffset.UtcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = null,
                PreserveTraceFile = false,
                SkipEndpointCertificateValidation = false,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
                RoleName = "RoleName",
                TriggerType = "HighCPU",
            });

            // Return null upon success.
            Assert.Null(actual);
        }

        [Fact]
        public void ShouldAcceptNullRoleName()
        {
            IUploadContextValidator target = new UploadContextValidator(fileExists: p => true);
            string actual = target.Validate(new UploadContext()
            {
                AIInstrumentationKey = Guid.NewGuid(),
                HostUrl = new Uri("https://endpoint"),
                SessionId = DateTimeOffset.UtcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = null,
                PreserveTraceFile = false,
                SkipEndpointCertificateValidation = false,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
                RoleName = null,
                TriggerType = "HighCPU",
            });

            // Return null upon success.
            Assert.Null(actual);
        }

        [Fact]
        public void ShouldAcceptOptionalTriggerType()
        {
            IUploadContextValidator target = new UploadContextValidator(fileExists: p => true);
            string actual = target.Validate(new UploadContext()
            {
                AIInstrumentationKey = Guid.NewGuid(),
                HostUrl = new Uri("https://endpoint"),
                SessionId = DateTimeOffset.UtcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = null,
                PreserveTraceFile = false,
                SkipEndpointCertificateValidation = false,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
                RoleName = "RoleName",
                TriggerType = "HighCPU"
            });

            // Return null upon success.
            Assert.Null(actual);
        }

        [Fact]
        public void ShouldAcceptNullTriggerType()
        {
            IUploadContextValidator target = new UploadContextValidator(fileExists: p => true);
            string actual = target.Validate(new UploadContext()
            {
                AIInstrumentationKey = Guid.NewGuid(),
                HostUrl = new Uri("https://endpoint"),
                SessionId = DateTimeOffset.UtcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = null,
                PreserveTraceFile = false,
                SkipEndpointCertificateValidation = false,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
                RoleName = "MyRole",
                TriggerType = null
            });

            // Return null upon success.
            Assert.Null(actual);
        }

        [Fact]
        public void ShouldFailWhenSampleFileNotExist()
        {
            string samplePath = @"c:\sample";
            IUploadContextValidator target = new UploadContextValidator(fileExists: p => !string.Equals(p, samplePath, StringComparison.Ordinal));
            string actual = target.Validate(new UploadContext()
            {
                AIInstrumentationKey = Guid.NewGuid(),
                HostUrl = new Uri("https://endpoint"),
                SessionId = DateTimeOffset.UtcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = null,
                PreserveTraceFile = false,
                SkipEndpointCertificateValidation = false,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = samplePath,
            });

            // Return null upon success.
            Assert.Equal($"Serialized sample file doesn't exist. File path: {samplePath}." + Environment.NewLine, actual);
        }

        [Fact]
        public void ShouldReturnErrorWhenNoInstrumentationKey()
        {
            TestImp(context => context.AIInstrumentationKey = Guid.Empty, "AIInstrumentationKey is required." + Environment.NewLine);
        }

        [Fact]
        public void ShouldReturnErrorWhenHostUriIsEmpty()
        {
            TestImp(context => context.HostUrl = null, "HostUrl is required." + Environment.NewLine);
        }

        [Fact]
        public void ShouldAtLeastHaveEitherNamedPipeNameOrSerializedSampleFilePathSet()
        {
            TestImp(context =>
            {
                context.PipeName = null;
                context.SerializedSampleFilePath = null;
            },
            "SerializedSampleFilePath and PipeName can't be null at the same time." + Environment.NewLine);
        }

        private void TestImp(Action<UploadContext> modify, string expectedError)
        {
            IUploadContextValidator target = new UploadContextValidator(fileExists: p => true);
            UploadContext context = new UploadContext()
            {
                AIInstrumentationKey = Guid.NewGuid(),
                HostUrl = new Uri("https://endpoint"),
                SessionId = DateTimeOffset.UtcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = null,
                PreserveTraceFile = false,
                SkipEndpointCertificateValidation = false,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
            };
            modify(context);
            string actual = target.Validate(context);

            Assert.Equal(expectedError, actual);
        }
    }
}
