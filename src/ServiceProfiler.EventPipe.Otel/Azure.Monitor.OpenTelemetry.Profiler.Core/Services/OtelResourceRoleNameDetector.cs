using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.Services;

internal class OtelResourceRoleNameDetector : IRoleNameDetector
{
    private readonly OtelResourceDetector _otelResourceDetector;

    public OtelResourceRoleNameDetector(OtelResourceDetector otelResourceDetector)
    {
        _otelResourceDetector = otelResourceDetector ?? throw new ArgumentNullException(nameof(otelResourceDetector));
    }

    public string? GetRoleName() => _otelResourceDetector.GetResource(OtelResourceSemanticConventions.AttributeServiceName);
}