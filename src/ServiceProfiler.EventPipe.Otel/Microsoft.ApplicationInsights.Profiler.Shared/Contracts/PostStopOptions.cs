//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ServiceProfiler.Orchestration;
using System;
using System.Collections.Generic;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Contracts;

internal class PostStopOptions
{
    public PostStopOptions(
        string traceFilePath,
        DateTimeOffset sessionId,
        Uri stampFrontendHostUrl,
        IEnumerable<SampleActivity> samples,
        IProfilerSource profilerSource,
        string? uploaderFullPath = null
        )
    {
        if (string.IsNullOrEmpty(traceFilePath))
        {
            throw new ArgumentException($"'{nameof(traceFilePath)}' cannot be null or empty.", nameof(traceFilePath));
        }

        TraceFilePath = traceFilePath;
        SessionId = sessionId;
        StampFrontendHostUrl = stampFrontendHostUrl;
        Samples = samples ?? throw new ArgumentNullException(nameof(samples));
        if (profilerSource is null)
        {
            throw new ArgumentNullException(nameof(profilerSource));
        }

        ProfilerSource = profilerSource.Source ?? throw new ArgumentNullException(nameof(profilerSource));
        UploaderFullPath = uploaderFullPath;
    }

    public string TraceFilePath { get; }
    public DateTimeOffset SessionId { get; }
    public Uri StampFrontendHostUrl { get; }
    public string ProfilerSource { get; }

    public string? UploaderFullPath { get; set; }
    public IEnumerable<SampleActivity> Samples { get; set; }
    public bool IsTraceValid { get; set; }
}
