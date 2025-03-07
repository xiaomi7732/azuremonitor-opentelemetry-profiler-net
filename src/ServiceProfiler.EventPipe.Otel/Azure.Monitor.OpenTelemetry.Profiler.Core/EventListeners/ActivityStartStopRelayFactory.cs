using Microsoft.Extensions.DependencyInjection;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

internal class ActivityStartStopRelayFactory
{
    private readonly IServiceProvider _serviceProvider;

    public ActivityStartStopRelayFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public ActivityStartStopRelay Create()
    {
        return ActivatorUtilities.CreateInstance<ActivityStartStopRelay>(_serviceProvider);
    }
}