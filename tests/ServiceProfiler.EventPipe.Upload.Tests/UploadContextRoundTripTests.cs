// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ServiceProfiler.EventPipe.Upload.Tests
{
    /// <summary>
    /// Verifies the uploader argument contract is honored without any third-party command
    /// line parser: a command line whose keys match the UploadContext property names binds
    /// back into an UploadContext via Microsoft.Extensions.Configuration. The exact wire
    /// format produced by UploadContextModel.ToString() is locked by UploadContextModelTests.
    /// </summary>
    public class UploadContextRoundTripTests
    {
        [Fact]
        public void CommandLine_BindsToUploadContext_AllFields()
        {
            Guid iKey = Guid.NewGuid();
            DateTimeOffset sessionId = new(2024, 6, 29, 13, 45, 30, TimeSpan.Zero);

            string commandLine =
                $@"--{nameof(UploadContext.TraceFilePath)} ""C:\traces\my trace.nettrace""" +
                $@" --{nameof(UploadContext.AIInstrumentationKey)} ""{iKey}""" +
                $@" --{nameof(UploadContext.SessionId)} ""{sessionId.UtcDateTime:o}""" +
                $@" --{nameof(UploadContext.StampId)} ""my-stamp""" +
                $@" --{nameof(UploadContext.HostUrl)} ""https://example.endpoint.com/""" +
                $@" --{nameof(UploadContext.MetadataFilePath)} ""C:\traces\meta data.json""" +
                $@" --{nameof(UploadContext.UploadMode)} ""{UploadMode.Always}""" +
                $@" --{nameof(UploadContext.SerializedSampleFilePath)} ""C:\traces\samples file.json""" +
                $@" --{nameof(UploadContext.PipeName)} ""pipe-name-123""" +
                $@" --{nameof(UploadContext.PreserveTraceFile)} true" +
                $@" --{nameof(UploadContext.SkipEndpointCertificateValidation)} true" +
                $@" --{nameof(UploadContext.RoleName)} ""my role name""" +
                $@" --{nameof(UploadContext.TriggerType)} ""my trigger""" +
                $@" --{nameof(UploadContext.TraceFileFormat)} ""Nettrace""";

            UploadContext bound = Bind(commandLine);

            Assert.Equal(iKey, bound.AIInstrumentationKey);
            Assert.Equal(new Uri("https://example.endpoint.com/"), bound.HostUrl);
            Assert.Equal(sessionId, bound.SessionId);
            Assert.Equal("my-stamp", bound.StampId);
            Assert.Equal(@"C:\traces\my trace.nettrace", bound.TraceFilePath);
            Assert.Equal(@"C:\traces\meta data.json", bound.MetadataFilePath);
            Assert.True(bound.PreserveTraceFile);
            Assert.True(bound.SkipEndpointCertificateValidation);
            Assert.Equal(UploadMode.Always, bound.UploadMode);
            Assert.Equal(@"C:\traces\samples file.json", bound.SerializedSampleFilePath);
            Assert.Equal("pipe-name-123", bound.PipeName);
            Assert.Equal("my role name", bound.RoleName);
            Assert.Equal("my trigger", bound.TriggerType);
            Assert.Equal("Nettrace", bound.TraceFileFormat);
            Assert.True(bound.UseNamedPipe);
        }

        [Fact]
        public void CommandLine_OmitsOptionalFields_BindsToDefaults()
        {
            Guid iKey = Guid.NewGuid();
            DateTimeOffset sessionId = new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

            string commandLine =
                $@"--{nameof(UploadContext.TraceFilePath)} ""/tmp/trace.nettrace""" +
                $@" --{nameof(UploadContext.AIInstrumentationKey)} ""{iKey}""" +
                $@" --{nameof(UploadContext.SessionId)} ""{sessionId.UtcDateTime:o}""" +
                $@" --{nameof(UploadContext.StampId)} ""stamp""" +
                $@" --{nameof(UploadContext.HostUrl)} ""https://example.endpoint.com/""" +
                $@" --{nameof(UploadContext.MetadataFilePath)} ""/tmp/meta.json""" +
                $@" --{nameof(UploadContext.UploadMode)} ""{UploadMode.OnSuccess}""" +
                $@" --{nameof(UploadContext.SerializedSampleFilePath)} ""/tmp/samples.json""" +
                $@" --{nameof(UploadContext.TraceFileFormat)} ""Netperf""";

            UploadContext bound = Bind(commandLine);

            Assert.Equal(UploadMode.OnSuccess, bound.UploadMode);
            Assert.False(bound.PreserveTraceFile);
            Assert.False(bound.SkipEndpointCertificateValidation);
            Assert.Null(bound.PipeName);
            Assert.Null(bound.RoleName);
            Assert.Null(bound.TriggerType);
            Assert.False(bound.UseNamedPipe);
        }

        private static UploadContext Bind(string commandLine)
        {
            string[] args = Tokenize(commandLine);
            IConfiguration configuration = new ConfigurationBuilder()
                .AddCommandLine(args)
                .Build();

            UploadContext? context = configuration.Get<UploadContext>();
            Assert.NotNull(context);
            return context!;
        }

        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        /// <summary>
        /// Splits a command line into argument tokens exactly the way the OS does when the
        /// uploader process is launched. <c>OutOfProcCaller</c> passes the string built
        /// by <c>UploadContextModel.ToString()</c> to <c>ProcessStartInfo.Arguments</c>, so on
        /// Windows the child parses it via the CRT / <c>CommandLineToArgvW</c> rules. We invoke
        /// that same API here (prefixing a dummy argv[0] for the executable name, then dropping
        /// it) so the test exercises real tokenization rather than an approximation.
        /// </summary>
        private static string[] Tokenize(string commandLine)
        {
            IntPtr argv = CommandLineToArgvW("uploader.exe " + commandLine, out int count);
            Assert.NotEqual(IntPtr.Zero, argv);

            try
            {
                // Skip index 0 (the synthetic executable name).
                string[] args = new string[count - 1];
                for (int i = 1; i < count; i++)
                {
                    IntPtr argPtr = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                    args[i - 1] = Marshal.PtrToStringUni(argPtr)!;
                }

                return args;
            }
            finally
            {
                LocalFree(argv);
            }
        }
    }
}
