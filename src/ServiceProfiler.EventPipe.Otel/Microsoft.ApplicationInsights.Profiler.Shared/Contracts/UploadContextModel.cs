using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Contracts;

internal class UploadContextModel
{
    public Guid AIInstrumentationKey { get; init; }

    public Uri HostUrl { get; init; } = null!;

    public DateTimeOffset SessionId { get; init; }

    public string StampId { get; init; } = null!;

    public string TraceFilePath { get; init; } = null!;

    public string? MetadataFilePath { get; init; }

    public bool PreserveTraceFile { get; init; } = false;

    public bool SkipEndpointCertificateValidation { get; init; } = false;

    public UploadMode UploadMode { get; init; } = UploadMode.OnSuccess;

    public string SerializedSampleFilePath { get; init; } = null!;

    public string? PipeName { get; init; }

    public string? RoleName { get; init; }

    public string Environment { get; init; } = "Production";

    public string? TriggerType { get; init; }
}