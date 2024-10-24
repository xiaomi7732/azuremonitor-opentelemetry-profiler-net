using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal static class UploadContextModelExtensions
{
    public static UploadContext CreateContext(this UploadContextModel model)
    {
        if (model is null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        return new UploadContext(){
            AIInstrumentationKey = model.AIInstrumentationKey,
            HostUrl = model.HostUrl,
            SessionId = model.SessionId,
            StampId = model.StampId,
            TraceFilePath = model.TraceFilePath,
            MetadataFilePath = model.MetadataFilePath,
            PreserveTraceFile = model.PreserveTraceFile,
            SkipEndpointCertificateValidation = model.SkipEndpointCertificateValidation,
            UploadMode = model.UploadMode,
            SerializedSampleFilePath = model.SerializedSampleFilePath,
            PipeName = model.PipeName,
            RoleName = model.RoleName,
            Environment = model.Environment,
            TriggerType = model.TriggerType,
        };
    }
}