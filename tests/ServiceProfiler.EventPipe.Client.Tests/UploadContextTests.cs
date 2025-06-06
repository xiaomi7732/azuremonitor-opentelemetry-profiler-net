using System;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ServiceProfiler.Contract.Agent;
using Xunit;

namespace ServiceProfiler.EventPipe.Client.Tests
{

    public class UploadContextTests
    {
        [Fact]
        public void ShouldOverwriteToStringForCommandLine()
        {
            Guid iKey = Guid.Parse("ed63033b-cd63-4df6-848e-f00772de729f");
            Guid dataCube = Guid.Parse("33a5a798-8489-4467-97d3-b35870fed1b3");
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;

            var validUploadContext = new UploadContext()
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
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = $@"-t ""c:\tracefilePath.etl.zip"" -i ed63033b-cd63-4df6-848e-f00772de729f --sessionId ""{TimestampContract.TimestampToString(utcNow)}"" -s ""stampId"" --host https://endpoint/ --metadata ""c:\metadataFilePath.metadata"" --uploadMode ""OnSuccess"" --sampleActivityFilePath ""c:\sample"" --traceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldSetPreserveTraceFile()
        {
            Guid iKey = Guid.Parse("ed63033b-cd63-4df6-848e-f00772de729f");
            Guid dataCube = Guid.Parse("33a5a798-8489-4467-97d3-b35870fed1b3");
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;

            var validUploadContext = new UploadContext()
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
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = $@"-t ""c:\tracefilePath.etl.zip"" -i ed63033b-cd63-4df6-848e-f00772de729f --sessionId ""{TimestampContract.TimestampToString(utcNow)}"" -s ""stampId"" --host https://endpoint/ --metadata ""c:\metadataFilePath.metadata"" --uploadMode ""OnSuccess"" --sampleActivityFilePath ""c:\sample"" --preserve --traceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldAllowInsecureSSLCommunication()
        {
            Guid iKey = Guid.Parse("ed63033b-cd63-4df6-848e-f00772de729f");
            Guid dataCube = Guid.Parse("33a5a798-8489-4467-97d3-b35870fed1b3");
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;

            var validUploadContext = new UploadContext()
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
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = $@"-t ""c:\tracefilePath.etl.zip"" -i ed63033b-cd63-4df6-848e-f00772de729f --sessionId ""{TimestampContract.TimestampToString(utcNow)}"" -s ""stampId"" --host https://endpoint/ --metadata ""c:\metadataFilePath.metadata"" --uploadMode ""OnSuccess"" --sampleActivityFilePath ""c:\sample"" --preserve --insecure --traceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldPassOnRoleNameWhenExists()
        {
            Guid iKey = Guid.Parse("ed63033b-cd63-4df6-848e-f00772de729f");
            Guid dataCube = Guid.Parse("33a5a798-8489-4467-97d3-b35870fed1b3");
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            const string roleName = "testRoleName";
            UploadContext validUploadContext = new UploadContext()
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
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = $@"-t ""c:\tracefilePath.etl.zip"" -i ed63033b-cd63-4df6-848e-f00772de729f --sessionId ""{TimestampContract.TimestampToString(utcNow)}"" -s ""stampId"" --host https://endpoint/ --metadata ""c:\metadataFilePath.metadata"" --uploadMode ""OnSuccess"" --sampleActivityFilePath ""c:\sample"" --preserve --insecure --roleName ""{roleName}"" --traceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldNotPassOnRoleNameWhenNull()
        {
            Guid iKey = Guid.Parse("ed63033b-cd63-4df6-848e-f00772de729f");
            Guid dataCube = Guid.Parse("33a5a798-8489-4467-97d3-b35870fed1b3");
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            UploadContext validUploadContext = new UploadContext()
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
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = $@"-t ""c:\tracefilePath.etl.zip"" -i ed63033b-cd63-4df6-848e-f00772de729f --sessionId ""{TimestampContract.TimestampToString(utcNow)}"" -s ""stampId"" --host https://endpoint/ --metadata ""c:\metadataFilePath.metadata"" --uploadMode ""OnSuccess"" --sampleActivityFilePath ""c:\sample"" --preserve --insecure --traceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldNotPassOnRoleNameWhenEmpty()
        {
            Guid iKey = Guid.Parse("ed63033b-cd63-4df6-848e-f00772de729f");
            Guid dataCube = Guid.Parse("33a5a798-8489-4467-97d3-b35870fed1b3");
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            UploadContext validUploadContext = new UploadContext()
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
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = $@"-t ""c:\tracefilePath.etl.zip"" -i ed63033b-cd63-4df6-848e-f00772de729f --sessionId ""{TimestampContract.TimestampToString(utcNow)}"" -s ""stampId"" --host https://endpoint/ --metadata ""c:\metadataFilePath.metadata"" --uploadMode ""OnSuccess"" --sampleActivityFilePath ""c:\sample"" --preserve --insecure --traceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldPassOnTriggerTypeWhenExists()
        {
            Guid iKey = Guid.Parse("ed63033b-cd63-4df6-848e-f00772de729f");
            Guid dataCube = Guid.Parse("33a5a798-8489-4467-97d3-b35870fed1b3");
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            const string trigger = "HighCPU";
            UploadContext validUploadContext = new UploadContext()
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
                TriggerType = trigger
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = $@"-t ""c:\tracefilePath.etl.zip"" -i ed63033b-cd63-4df6-848e-f00772de729f --sessionId ""{TimestampContract.TimestampToString(utcNow)}"" -s ""stampId"" --host https://endpoint/ --metadata ""c:\metadataFilePath.metadata"" --uploadMode ""OnSuccess"" --sampleActivityFilePath ""c:\sample"" --preserve --insecure --trigger ""{trigger}"" --traceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldNotPassOnTriggerTypeWhenNull()
        {
            Guid iKey = Guid.Parse("ed63033b-cd63-4df6-848e-f00772de729f");
            Guid dataCube = Guid.Parse("33a5a798-8489-4467-97d3-b35870fed1b3");
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            UploadContext validUploadContext = new UploadContext()
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
                TriggerType = null
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = $@"-t ""c:\tracefilePath.etl.zip"" -i ed63033b-cd63-4df6-848e-f00772de729f --sessionId ""{TimestampContract.TimestampToString(utcNow)}"" -s ""stampId"" --host https://endpoint/ --metadata ""c:\metadataFilePath.metadata"" --uploadMode ""OnSuccess"" --sampleActivityFilePath ""c:\sample"" --preserve --insecure --traceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }

        [Fact]
        public void ShouldNotPassOnTriggerTypeWhenEmpty()
        {
            Guid iKey = Guid.Parse("ed63033b-cd63-4df6-848e-f00772de729f");
            Guid dataCube = Guid.Parse("33a5a798-8489-4467-97d3-b35870fed1b3");
            DateTimeOffset utcNow = DateTimeOffset.UtcNow;
            UploadContext validUploadContext = new UploadContext()
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
            };
            string commandLine = validUploadContext.ToString();

            string expectedCommandLine = $@"-t ""c:\tracefilePath.etl.zip"" -i ed63033b-cd63-4df6-848e-f00772de729f --sessionId ""{TimestampContract.TimestampToString(utcNow)}"" -s ""stampId"" --host https://endpoint/ --metadata ""c:\metadataFilePath.metadata"" --uploadMode ""OnSuccess"" --sampleActivityFilePath ""c:\sample"" --preserve --insecure --traceFileFormat ""Netperf""";

            Assert.Equal(expectedCommandLine, commandLine);
        }
    }
}
