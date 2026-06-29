//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Core.Contracts
{
    internal class UploadContext
    {
        public Guid AIInstrumentationKey { get; set; }

        public Uri HostUrl { get; set; } = null!;

        public DateTimeOffset SessionId { get; set; }

        public string StampId { get; set; } = null!;

        public string TraceFilePath { get; set; } = null!;

        public string MetadataFilePath { get; set; } = null!;

        public bool PreserveTraceFile { get; set; }

        public bool SkipEndpointCertificateValidation { get; set; }

        public UploadMode UploadMode { get; set; } = UploadMode.OnSuccess;

        public string SerializedSampleFilePath { get; set; } = null!;

        public string? PipeName { get; set; }

        public string? RoleName { get; set; }

        public string? TriggerType { get; set; }

        public string TraceFileFormat { get; set; } = ServiceProfiler.Contract.Agent.Profiler.TraceFileFormat.Netperf;

        public bool UseNamedPipe => !string.IsNullOrEmpty(PipeName);
    }
}
