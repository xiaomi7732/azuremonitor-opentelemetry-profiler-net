using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.Services;

internal class OtelResourceRoleInstanceDetector : IRoleInstanceDetector
{
    private readonly OtelResourceDetector _otelResourceDetector;

    public OtelResourceRoleInstanceDetector(OtelResourceDetector otelResourceDetector)
    {
        _otelResourceDetector = otelResourceDetector ?? throw new ArgumentNullException(nameof(otelResourceDetector));
    }

    public string? GetRoleInstance() => _otelResourceDetector.GetResource(OtelResourceSemanticConventions.AttributeServiceInstance);
}