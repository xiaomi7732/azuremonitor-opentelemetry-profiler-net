using System;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ServiceProfiler.Contract.Agent;
using Xunit;

namespace ServiceProfiler.EventPipe.Client.Tests
{

    public class UploadContextModelTests
    {
        private const string IKey = "ed63033b-cd63-4df6-848e-f00772de729f";

        private static string CommonPrefix(DateTimeOffset utcNow) =>
            $@"--TraceFilePath ""c:\tracefilePath.etl.zip"" --AIInstrumentationKey ""{IKey}"" --SessionId ""{TimestampContract.TimestampToString(utcNow)}"" --StampId ""stampId"" --HostUrl ""https://endpoint/"" --MetadataFilePath ""c:\metadataFilePath.metadata"" --UploadMode ""OnSuccess"" --SerializedSampleFilePath ""c:\sample""";

        [Fact]
        public void ShouldOverwriteToStringForCommandLine()
        {
            Guid iKey = Guid.Parse(IKey);
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;

            var validUploadContext = new UploadContextModel()
            {
                AIInstrumentationKey = iKey,
                HostUrl = new Uri("https://endpoint", UriKind.Absolute),
                SessionId = utcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = @"c:\metadataFilePath.metadata",
                PreserveTraceFile = false,
                SkipEndpointCertificateValidation = false,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
                TraceFileFormat = "Netperf",
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = CommonPrefix(utcNow) + @" --TraceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldSetPreserveTraceFile()
        {
            Guid iKey = Guid.Parse(IKey);
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;

            var validUploadContext = new UploadContextModel()
            {
                AIInstrumentationKey = iKey,
                HostUrl = new Uri("https://endpoint", UriKind.Absolute),
                SessionId = utcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = @"c:\metadataFilePath.metadata",
                PreserveTraceFile = true,
                SkipEndpointCertificateValidation = false,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
                TraceFileFormat = "Netperf",
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = CommonPrefix(utcNow) + @" --PreserveTraceFile true --TraceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldAllowInsecureSSLCommunication()
        {
            Guid iKey = Guid.Parse(IKey);
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;

            var validUploadContext = new UploadContextModel()
            {
                AIInstrumentationKey = iKey,
                HostUrl = new Uri("https://endpoint", UriKind.Absolute),
                SessionId = utcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = @"c:\metadataFilePath.metadata",
                PreserveTraceFile = true,
                SkipEndpointCertificateValidation = true,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
                TraceFileFormat = "Netperf",
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = CommonPrefix(utcNow) + @" --PreserveTraceFile true --SkipEndpointCertificateValidation true --TraceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldPassOnRoleNameWhenExists()
        {
            Guid iKey = Guid.Parse(IKey);
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            const string roleName = "testRoleName";
            UploadContextModel validUploadContext = new()
            {
                AIInstrumentationKey = iKey,
                HostUrl = new Uri("https://endpoint", UriKind.Absolute),
                SessionId = utcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = @"c:\metadataFilePath.metadata",
                PreserveTraceFile = true,
                SkipEndpointCertificateValidation = true,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
                RoleName = roleName,
                TraceFileFormat = "Netperf",
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = CommonPrefix(utcNow) + $@" --PreserveTraceFile true --SkipEndpointCertificateValidation true --RoleName ""{roleName}"" --TraceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldNotPassOnRoleNameWhenNull()
        {
            Guid iKey = Guid.Parse(IKey);
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            UploadContextModel validUploadContext = new()
            {
                AIInstrumentationKey = iKey,
                HostUrl = new Uri("https://endpoint", UriKind.Absolute),
                SessionId = utcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = @"c:\metadataFilePath.metadata",
                PreserveTraceFile = true,
                SkipEndpointCertificateValidation = true,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
                RoleName = null,
                TraceFileFormat = "Netperf",
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = CommonPrefix(utcNow) + @" --PreserveTraceFile true --SkipEndpointCertificateValidation true --TraceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldNotPassOnRoleNameWhenEmpty()
        {
            Guid iKey = Guid.Parse(IKey);
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            UploadContextModel validUploadContext = new()
            {
                AIInstrumentationKey = iKey,
                HostUrl = new Uri("https://endpoint", UriKind.Absolute),
                SessionId = utcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = @"c:\metadataFilePath.metadata",
                PreserveTraceFile = true,
                SkipEndpointCertificateValidation = true,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
                RoleName = string.Empty,
                TraceFileFormat = "Netperf",
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = CommonPrefix(utcNow) + @" --PreserveTraceFile true --SkipEndpointCertificateValidation true --TraceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldPassOnTriggerTypeWhenExists()
        {
            Guid iKey = Guid.Parse(IKey);
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            const string trigger = "HighCPU";
            UploadContextModel validUploadContext = new UploadContextModel()
            {
                AIInstrumentationKey = iKey,
                HostUrl = new Uri("https://endpoint", UriKind.Absolute),
                SessionId = utcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = @"c:\metadataFilePath.metadata",
                PreserveTraceFile = true,
                SkipEndpointCertificateValidation = true,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
                TriggerType = trigger,
                TraceFileFormat = "Netperf",
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = CommonPrefix(utcNow) + $@" --PreserveTraceFile true --SkipEndpointCertificateValidation true --TriggerType ""{trigger}"" --TraceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldNotPassOnTriggerTypeWhenNull()
        {
            Guid iKey = Guid.Parse(IKey);
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            UploadContextModel validUploadContext = new UploadContextModel()
            {
                AIInstrumentationKey = iKey,
                HostUrl = new Uri("https://endpoint", UriKind.Absolute),
                SessionId = utcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = @"c:\metadataFilePath.metadata",
                PreserveTraceFile = true,
                SkipEndpointCertificateValidation = true,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
                TriggerType = null,
                TraceFileFormat = "Netperf",
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = CommonPrefix(utcNow) + @" --PreserveTraceFile true --SkipEndpointCertificateValidation true --TraceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldNotPassOnTriggerTypeWhenEmpty()
        {
            Guid iKey = Guid.Parse(IKey);
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            UploadContextModel validUploadContext = new()
            {
                AIInstrumentationKey = iKey,
                HostUrl = new Uri("https://endpoint", UriKind.Absolute),
                SessionId = utcNow,
                StampId = "stampId",
                TraceFilePath = @"c:\tracefilePath.etl.zip",
                MetadataFilePath = @"c:\metadataFilePath.metadata",
                PreserveTraceFile = true,
                SkipEndpointCertificateValidation = true,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"c:\sample",
                TriggerType = string.Empty,
                TraceFileFormat = "Netperf",
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = CommonPrefix(utcNow) + @" --PreserveTraceFile true --SkipEndpointCertificateValidation true --TraceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }
    }
}
