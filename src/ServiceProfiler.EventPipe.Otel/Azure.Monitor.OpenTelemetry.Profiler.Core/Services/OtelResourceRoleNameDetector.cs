using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal class OtelResourceRoleNameDetector : IRoleNameDetector
{
    private const string ServiceInstanceResourceId = "service.instance.id";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    public OtelResourceRoleNameDetector(
        IServiceProvider serviceProvider,
        ILogger<OtelResourceRoleNameDetector> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string? GetRoleName()
    {
        TracerProvider? tracerProvider = _serviceProvider.GetService<TracerProvider>();
        if (tracerProvider is null)
        {
            _logger.LogWarning("TracerProvider is not registered. Cannot get the role name from OpenTelemetry.");
            return null;
        }

        Resource otelResource = tracerProvider.GetResource();


        object? roleNameObject = otelResource.Attributes.FirstOrDefault(attribute => string.Equals(attribute.Key, ServiceInstanceResourceId, StringComparison.OrdinalIgnoreCase)).Value;
        if (roleNameObject is null)
        {
            _logger.LogDebug("Role name is not found in the OpenTelemetry resource. Cannot get the role name from OpenTelemetry.");
            return null;
        }
        
        return roleNameObject.ToString() ?? string.Empty;
    }
}