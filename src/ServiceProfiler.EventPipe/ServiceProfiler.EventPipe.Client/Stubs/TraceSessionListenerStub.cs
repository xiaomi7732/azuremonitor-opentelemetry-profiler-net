//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Core.EventListeners;
using Microsoft.ApplicationInsights.Profiler.Core.Sampling;
using Microsoft.ApplicationInsights.Profiler.Core.TraceControls;
using Microsoft.ApplicationInsights.Profiler.Core.Utilities;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Core.Stubs
{
    internal sealed class TraceSessionListenerStub : TraceSessionListener
    {
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
            _userConfiguration = userConfiguration.Value ?? throw new ArgumentNullException(nameof(userConfiguration));
            _traceConfiguration = new DiagnosticsClientTraceConfiguration(userConfiguration, new NullLogger<DiagnosticsClientTraceConfiguration>());
            _ctorFinishHandle.Set();
        }

        public void AddSampleActivity(SampleActivity activity = null)
        {
            activity = activity ?? new SampleActivity()
            {
                StartActivityIdPath = "StartActivityIdPath",
                StopActivityIdPath = "StopActivityIdPath",
                OperationId = "OperationId",
                OperationName = "Operation Name",
                RequestId = "Request Id",
                RoleInstance = "Role Instance",
                StartTimeUtc = DateTimeOffset.UtcNow,
                StopTimeUtc = DateTimeOffset.UtcNow,
                Duration = TimeSpan.FromSeconds(0.5),
            };

            SampleActivities.AddSample(activity);
        }

        #region private
        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            if (isDisposing)
            {
                _ctorFinishHandle?.Dispose();
                _ctorFinishHandle = null;
            }

            _logger.LogDebug("Disposing {0}", nameof(TraceSessionListenerStub));
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            Task.Run(() =>
            {
                _ctorFinishHandle.WaitOne();
                base.OnEventSourceCreated(eventSource);
                _logger.LogDebug("Candidate EventSource: {0} :: {1:D}", eventSource.Name, eventSource.Guid);

                EventPipeProvider p = _traceConfiguration.Providers.FirstOrDefault(
                    t => t.Name.Equals(eventSource.Name, StringComparison.Ordinal));
                if (p != null)
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
        }

        private readonly UserConfiguration _userConfiguration;
        private readonly ILogger _logger;
        private readonly DiagnosticsClientTraceConfiguration _traceConfiguration;
        // Due to a known issue, OnEventSourceCreated might be called before ctor finishes.
        // That cause accessing fields like _traceConfiguration in the event handler unreliable.
        // Put a event wait handle, set it until ctor is done and make OnEventSourceCreated waiting on it.
        // Potential issue is that the event handle code being dispatched on to a different thread.
        private EventWaitHandle _ctorFinishHandle = new ManualResetEvent(false);
        #endregion
    }
}
