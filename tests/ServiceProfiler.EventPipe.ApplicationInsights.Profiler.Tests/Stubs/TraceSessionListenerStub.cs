//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Core.EventListeners;
using Microsoft.ApplicationInsights.Profiler.Core.TraceControls;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Samples;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics.Tracing;
using System.Text.Json;

namespace ServiceProfiler.EventPipe.Client.Tests;

internal sealed class TraceSessionListenerStub : TraceSessionListener
{
    private readonly ILogger _logger;
    private readonly DiagnosticsClientTraceConfiguration _traceConfiguration;
    // Due to a known issue, OnEventSourceCreated might be called before ctor finishes.
    // That cause accessing fields like _traceConfiguration in the event handler unreliable.
    // Put a event wait handle, set it until ctor is done and make OnEventSourceCreated waiting on it.
    // Potential issue is that the event handle code being dispatched on to a different thread.
    private readonly EventWaitHandle _ctorFinishHandle = new ManualResetEvent(false);
    // Track pending OnEventSourceCreated tasks to avoid deadlock during dispose.
    // Dispose must wait for all EnableEvents calls to complete before calling base.Dispose(),
    // which acquires EventListener locks that conflict with EnableEvents locks.
    private readonly List<Task> _pendingTasks = new();
    private volatile bool _isDisposing;
    private List<EventPipeProvider> _eventPipeProviders => _traceConfiguration.BuildEventPipeProviders().ToList();

    public TraceSessionListenerStub(
        SampleActivityContainerFactory sampleActivityContainerFactory,
        IOptions<UserConfiguration> userConfiguration,
        ISerializationProvider serializer,
        ISerializationOptionsProvider<JsonSerializerOptions> serializerOptions,
        ILogger<TraceSessionListenerStub> logger)
        : base(sampleActivityContainerFactory, serializer, serializerOptions, logger)
    {
        _logger = logger;
        _logger.LogDebug("[{0:O}] Constructor of {1}.", DateTime.Now, nameof(TraceSessionListenerStub));
        _traceConfiguration = new DiagnosticsClientTraceConfiguration(userConfiguration, new NullLogger<DiagnosticsClientTraceConfiguration>());
        _ctorFinishHandle.Set();
    }

    public void AddSampleActivity(SampleActivity? activity = null)
    {
        activity = activity ?? new SampleActivity()
        {
            StartActivityIdPath = "StartActivityIdPath",
            StopActivityIdPath = "StopActivityIdPath",
            OperationId = "OperationId",
            OperationName = "Operation Name",
            RequestId = "Request Id",
            StartTimeUtc = DateTimeOffset.UtcNow,
            StopTimeUtc = DateTimeOffset.UtcNow,
            Duration = TimeSpan.FromSeconds(0.5),
        };

        SampleActivities.AddSample(activity);
    }

    #region private
    public override void Dispose()
    {
        _isDisposing = true;

        // Wait for all pending OnEventSourceCreated tasks to complete BEFORE calling
        // base.Dispose(). EventListener.Dispose() acquires internal EventSource locks to
        // remove this listener from all sources. If a pending Task.Run is concurrently calling
        // EnableEvents (which acquires the same locks in a different order), we get an AB-BA
        // deadlock. Draining the tasks first ensures no EnableEvents calls are in flight.
        Task[] pending;
        lock (_pendingTasks)
        {
            pending = _pendingTasks.ToArray();
        }
        if (pending.Length > 0)
        {
            Task.WaitAll(pending, TimeSpan.FromSeconds(5));
        }

        base.Dispose();
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        if (isDisposing)
        {
            _ctorFinishHandle.Dispose();
        }

        _logger.LogDebug("Disposing {0}", nameof(TraceSessionListenerStub));
    }

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        var task = Task.Run(() =>
        {
            _ctorFinishHandle.WaitOne();
            if (_isDisposing) return;

            base.OnEventSourceCreated(eventSource);
            _logger.LogDebug("Candidate EventSource: {0} :: {1:D}", eventSource.Name, eventSource.Guid);

            List<EventPipeProvider> eventPipeProviders = _traceConfiguration.BuildEventPipeProviders().ToList();

            EventPipeProvider? p = _eventPipeProviders.FirstOrDefault(t => t.Name.Equals(eventSource.Name, StringComparison.Ordinal));
            if (p is not null && !_isDisposing)
            {
                if (!eventSource.IsEnabled())
                {
                    EnableEvents(eventSource, (EventLevel)p.EventLevel, (EventKeywords)p.Keywords);
                    _logger.LogDebug("[{0:O}] Enabling EventSource: {1}", DateTime.Now, eventSource.Name);
                }
                else
                {
                    _logger.LogDebug("Already enabled event source: {0}", eventSource.Name);
                }
            }
        });

        lock (_pendingTasks)
        {
            _pendingTasks.Add(task);
        }

        task.ContinueWith(_ =>
        {
            lock (_pendingTasks)
            {
                _pendingTasks.Remove(task);
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }
    #endregion
}
