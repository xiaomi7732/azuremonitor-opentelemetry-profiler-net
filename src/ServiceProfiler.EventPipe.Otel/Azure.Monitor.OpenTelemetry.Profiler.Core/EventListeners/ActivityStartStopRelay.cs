using Microsoft.ApplicationInsights.Profiler.Shared.Samples;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core.EventListeners;

internal sealed class ActivityStartStopRelay : IDisposable
{
    private bool _hasActivityReported = false;
    private readonly ActivityListener _activityListener;
    private readonly ILogger<ActivityStartStopRelay> _logger;
    private readonly SampleCollector _sampleCollector;

    public SampleActivityContainer? SampleActivities => _sampleCollector?.SampleActivities;

    public ActivityStartStopRelay(
        SampleCollector sampleCollector,
        ILogger<ActivityStartStopRelay> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogDebug("ctor: {name}", nameof(ActivityStartStopRelay));

        _sampleCollector = sampleCollector ?? throw new ArgumentNullException(nameof(sampleCollector));

        _activityListener = new ActivityListener();

        _activityListener.ShouldListenTo += OnShouldListenTo;
        _activityListener.ActivityStarted += OnStarted;
        _activityListener.ActivityStopped += OnStopped;

        ActivitySource.AddActivityListener(_activityListener);
    }

    private bool OnShouldListenTo(ActivitySource source)
    {
        _logger.LogTrace("Activity Source Name: {name}", source.Name);

        if (string.IsNullOrEmpty(source.Name))
        {
            return false;
        }

        return true;
    }

    private void OnStarted(Activity activity)
    {
        // Accessing activity.Id here will cause the Id to be initialized
        // before the sampler runs in case where the activity is created using legacy way
        // i.e. new Activity("Operation name"). This will result in Id not reflecting the
        // correct sampling flags
        // https://github.com/dotnet/runtime/issues/61857

        string name = activity.DisplayName;
        (string activityId, string operationId, string requestId) = GetIds(activity);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Request started: Name: {name}, Source: {sourceName} Activity Id: {activityId}, Operation Id: {operationId}, Request Id: {requestId}", name, activity.Source.Name, activityId, operationId, requestId);
        }

        AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.Log.RequestStart(
            name,
            id: activityId,
             requestId,
             operationId,
             activity.StartTimeUtc);
    }

    private void OnStopped(Activity activity)
    {
        string name = activity.DisplayName;
        (string activityId, string operationId, string requestId) = GetIds(activity);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Request stopped: Name: {name}, Source: {sourceName}, Activity Id: {activityId}, Operation Id: {operationId}, Request Id: {requestId}", name, activity.Source.Name, activityId, operationId, requestId);
        }

        AzureMonitorOpenTelemetryProfilerDataAdapterEventSource.Log.RequestStop(
            name: name,
            id: activityId,
            requestId,
            operationId,
            activityStopTimeUtc: activity.StartTimeUtc + activity.Duration);

        if (!_hasActivityReported && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Activity detected.");
            _hasActivityReported = true;
        }
    }

    private (string activityId, string operationId, string requestId) GetIds(Activity activity)
    {
        string operationId = activity.TraceId.ToHexString();
        string requestId = activity.SpanId.ToHexString();
        string activityId = string.Concat("00-", operationId, "-", requestId);
        activityId = string.Concat(activityId, activity.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded) ? "-01" : "-00");
        return (activityId, operationId, requestId);
    }

    public void Dispose()
    {
        _activityListener.ActivityStarted -= OnStarted;
        _activityListener.ActivityStopped -= OnStopped;
        _activityListener.ShouldListenTo -= OnShouldListenTo;

        _activityListener?.Dispose();
    }
}