using System;
using System.Text;
using Microsoft.ServiceProfiler.Contract.Agent;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Contracts;

internal class UploadContextModel
{
    public Guid AIInstrumentationKey { get; init; }

    public Uri HostUrl { get; init; } = null!;

    public DateTimeOffset SessionId { get; init; }

    public string? StampId { get; init; }

    public string TraceFilePath { get; init; } = null!;

    public string? MetadataFilePath { get; init; }

    public bool PreserveTraceFile { get; init; } = false;

    public bool SkipEndpointCertificateValidation { get; init; } = false;

    public UploadMode UploadMode { get; init; } = UploadMode.OnSuccess;

    public string? SerializedSampleFilePath { get; init; }

    public string? PipeName { get; init; }

    public string? RoleName { get; init; }

    public string? TriggerType { get; init; }

    public string TraceFileFormat { get; set; } = null!;

    public override string ToString()
    {
        // Serializes the parameters into a command line that the Uploader binds back
        // into its own context via Microsoft.Extensions.Configuration.CommandLine.
        // Keys intentionally match the Uploader's UploadContext property names so the
        // configuration binder can map them directly (no third-party parser needed).
        StringBuilder builder = new();

        builder.Append($@"--{nameof(TraceFilePath)} ""{TraceFilePath}""");
        builder.Append($@" --{nameof(AIInstrumentationKey)} ""{AIInstrumentationKey}""");
        builder.Append($@" --{nameof(SessionId)} ""{TimestampContract.TimestampToString(SessionId)}""");
        builder.Append($@" --{nameof(StampId)} ""{StampId}""");
        builder.Append($@" --{nameof(HostUrl)} ""{HostUrl}""");
        builder.Append($@" --{nameof(MetadataFilePath)} ""{MetadataFilePath}""");
        builder.Append($@" --{nameof(UploadMode)} ""{UploadMode}""");
        builder.Append($@" --{nameof(SerializedSampleFilePath)} ""{SerializedSampleFilePath}""");

        if (!string.IsNullOrEmpty(PipeName))
        {
            builder.Append($@" --{nameof(PipeName)} ""{PipeName}""");
        }

        if (PreserveTraceFile)
        {
            builder.Append($" --{nameof(PreserveTraceFile)} true");
        }

        if (SkipEndpointCertificateValidation)
        {
            builder.Append($" --{nameof(SkipEndpointCertificateValidation)} true");
        }

        if (!string.IsNullOrEmpty(RoleName))
        {
            builder.Append($@" --{nameof(RoleName)} ""{RoleName}""");
        }

        if (!string.IsNullOrEmpty(TriggerType))
        {
            builder.Append($@" --{nameof(TriggerType)} ""{TriggerType}""");
        }

        builder.Append($@" --{nameof(TraceFileFormat)} ""{TraceFileFormat}""");

        return builder.ToString();
    }
}