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
    /// Genuine producer-to-consumer round trip: constructs the real <see cref="UploadContextModel"/>
    /// (the agent-side producer), serializes it with <see cref="UploadContextModel.ToString()"/>,
    /// tokenizes exactly the way the launched uploader process would (Windows
    /// <c>CommandLineToArgvW</c>), and binds the result back into the uploader's <see cref="UploadContext"/>
    /// via Microsoft.Extensions.Configuration. This verifies the actual wire contract this package changed -
    /// if the producer and consumer ever drift (key names, quoting, bool emission, defaults, timestamp
    /// format), these tests fail. The exact string emitted by the producer is additionally locked by
    /// UploadContextModelTests.
    /// </summary>
    public class UploadContextRoundTripTests
    {
        [WindowsOnlyFact]
        public void ProducerOutput_RoundTripsToUploadContext_AllFields()
        {
            // Use a session id with sub-second precision so a lossy timestamp format would be caught.
            DateTimeOffset sessionId = new DateTimeOffset(2024, 6, 29, 13, 45, 30, TimeSpan.Zero).AddTicks(1234567);

            UploadContextModel model = new()
            {
                AIInstrumentationKey = Guid.NewGuid(),
                HostUrl = new Uri("https://example.endpoint.com/"),
                SessionId = sessionId,
                StampId = "my-stamp",
                TraceFilePath = @"C:\traces\my trace.nettrace",
                MetadataFilePath = @"C:\traces\meta data.json",
                PreserveTraceFile = true,
                SkipEndpointCertificateValidation = true,
                UploadMode = UploadMode.Always,
                SerializedSampleFilePath = @"C:\traces\samples file.json",
                PipeName = "pipe-name-123",
                RoleName = "my role name",
                TriggerType = "my trigger",
                TraceFileFormat = "Nettrace",
            };

            UploadContext bound = RoundTrip(model);

            Assert.Equal(model.AIInstrumentationKey, bound.AIInstrumentationKey);
            Assert.Equal(model.HostUrl, bound.HostUrl);
            // Pins the timestamp format: the producer serializes via TimestampContract and the binder must
            // parse back the exact same instant (guards against an offset-less form shifting the value).
            Assert.Equal(model.SessionId, bound.SessionId);
            Assert.Equal(model.StampId, bound.StampId);
            Assert.Equal(model.TraceFilePath, bound.TraceFilePath);
            Assert.Equal(model.MetadataFilePath, bound.MetadataFilePath);
            Assert.Equal(model.PreserveTraceFile, bound.PreserveTraceFile);
            Assert.Equal(model.SkipEndpointCertificateValidation, bound.SkipEndpointCertificateValidation);
            Assert.Equal(model.UploadMode, bound.UploadMode);
            Assert.Equal(model.SerializedSampleFilePath, bound.SerializedSampleFilePath);
            Assert.Equal(model.PipeName, bound.PipeName);
            Assert.Equal(model.RoleName, bound.RoleName);
            Assert.Equal(model.TriggerType, bound.TriggerType);
            Assert.Equal(model.TraceFileFormat, bound.TraceFileFormat);
            Assert.True(bound.UseNamedPipe);
        }

        [WindowsOnlyFact]
        public void ProducerOutput_WithOptionalFieldsOmitted_RoundTrips()
        {
            DateTimeOffset sessionId = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(7654321);

            // PipeName / RoleName / TriggerType null and the bool flags false: the producer omits these
            // entirely, so the consumer must fall back to its defaults rather than binding stale values.
            UploadContextModel model = new()
            {
                AIInstrumentationKey = Guid.NewGuid(),
                HostUrl = new Uri("https://example.endpoint.com/"),
                SessionId = sessionId,
                StampId = "stamp",
                TraceFilePath = @"/tmp/trace.nettrace",
                MetadataFilePath = @"/tmp/meta.json",
                PreserveTraceFile = false,
                SkipEndpointCertificateValidation = false,
                UploadMode = UploadMode.OnSuccess,
                SerializedSampleFilePath = @"/tmp/samples.json",
                PipeName = null,
                RoleName = null,
                TriggerType = null,
                TraceFileFormat = "Netperf",
            };

            UploadContext bound = RoundTrip(model);

            Assert.Equal(model.SessionId, bound.SessionId);
            Assert.Equal(UploadMode.OnSuccess, bound.UploadMode);
            Assert.False(bound.PreserveTraceFile);
            Assert.False(bound.SkipEndpointCertificateValidation);
            Assert.Null(bound.PipeName);
            Assert.Null(bound.RoleName);
            Assert.Null(bound.TriggerType);
            Assert.False(bound.UseNamedPipe);
            Assert.Equal(model.TraceFileFormat, bound.TraceFileFormat);
        }

        private static UploadContext RoundTrip(UploadContextModel model)
        {
            string[] args = Tokenize(model.ToString());
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
