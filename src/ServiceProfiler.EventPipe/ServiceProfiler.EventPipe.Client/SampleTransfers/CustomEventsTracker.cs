//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.ApplicationInsights.Profiler.Core.EventListeners;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Contract;
using Microsoft.ServiceProfiler.Contract.Agent;
using Microsoft.ServiceProfiler.Orchestration;
using Microsoft.ServiceProfiler.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Microsoft.ApplicationInsights.Profiler.Core.SampleTransfers;
internal class CustomEventsTracker : ICustomEventsTracker, IRoleNameSource
{
    public CustomEventsTracker(
        IServiceProfilerContext serviceProfilerContext,
        ICustomTelemetryClientFactory customTelemetryClientFactory,
        IResourceUsageSource resourceUsage,
        ISerializationProvider serializer,
        ILogger<CustomEventsTracker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _context = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _resourceUsage = resourceUsage ?? throw new ArgumentNullException(nameof(resourceUsage));
        _telemetryClient = (customTelemetryClientFactory ?? throw new ArgumentNullException(nameof(customTelemetryClientFactory))).Create();
    }

    public int Send(IEnumerable<SampleActivity> samples, UploadContext uploadContext, int processId, string profilingSource, Guid verifiedDataCube)
    {
        _logger.LogTrace("Start to send samples to AI.");

        if (samples is null)
        {
            throw new ArgumentNullException(nameof(samples));
        }

        if (uploadContext is null)
        {
            throw new ArgumentNullException(nameof(uploadContext));
        }

        uploadContext.StampId = uploadContext.StampId ?? throw new NullReferenceException(nameof(uploadContext.StampId));

        if (string.IsNullOrEmpty(profilingSource))
        {
            throw new ArgumentException($"'{nameof(profilingSource)}' cannot be null or empty.", nameof(profilingSource));
        }

        int customEventCount = 0;
        if (samples.Any())
        {
            string machineName = EnvironmentUtilities.MachineName;
            foreach (SampleActivity sample in samples)
            {
                SendCustomEventToAI(sample, uploadContext.StampId, processId, uploadContext.SessionId, verifiedDataCube, machineName);
                customEventCount++;
            }

            _logger.LogDebug("{0} events has been sent to AI as CustomEvents", customEventCount);

            // Send Index event
            string fileId = EnvironmentUtilities.CreateSessionId();
            SendIndexEventToAI(
                fileId,
                uploadContext.StampId,
                verifiedDataCube,
                uploadContext.SessionId,
                processId, profilingSource,
                Environment.OSVersion.VersionString,
                _resourceUsage.GetAverageCPUUsage(),
                _resourceUsage.GetAverageMemoryUsage(),
                machineName);
            _logger.LogDebug("Index event has been sent to AI.");

            _telemetryClient.Flush();
        }
        else
        {
            _logger.LogDebug("No samples to upload.");
        }

        return customEventCount;
    }

    public string CloudRoleName => _roleNameCache ??= GetCloudContext()?.RoleName;

    private CloudContext GetCloudContext()
    {
        // we need to initialize an item to get instance information
        EventTelemetry fakeItem = new();
        try
        {
            _telemetryClient.Initialize(fakeItem);
        }
#pragma warning disable CA1031
        catch (Exception)
        {
            // we don't care what happened there
        }
#pragma warning restore CA1031

        CloudContext cloudContext = fakeItem.Context?.Cloud;
        _logger.LogDebug("Fetched rolename: {roleName}, roleInstance: {roleInstance}", cloudContext?.RoleName, cloudContext?.RoleInstance);
        return cloudContext;
    }

    private void SendIndexEventToAI(
        string fileId,
        string stampId,
        Guid dataCube,
        DateTimeOffset sessionId,
        int processId,
        string source,
        string operatingSystem,
        float averageCPUUsage,
        float averageMemoryUsage,
        string machineName)
    {
        EventTelemetry indexEvent = new(ServiceProfilerIndex);

        indexEvent.Properties[FileId] = fileId;
        indexEvent.Properties[StampId] = stampId;
        indexEvent.Properties[DataCube] = StoragePathContract.GetDataCubeNameString(dataCube);
        indexEvent.Properties[EtlFileSessionId] = TimestampContract.TimestampToString(sessionId);
        indexEvent.Properties[MachineName] = machineName;
        indexEvent.Properties[ProcessId] = processId.ToString(CultureInfo.InvariantCulture);

        // More info here.
        indexEvent.Properties[Source] = source;
        indexEvent.Properties[OperatingSystem] = operatingSystem;
        indexEvent.Metrics[AverageCPUUsage] = averageCPUUsage;
        indexEvent.Metrics[AverageMemoryUsage] = averageMemoryUsage;
        indexEvent.Context.Cloud.RoleName ??= CloudRoleName;

        SendCustomEventToAI(indexEvent);
    }

    private void SendCustomEventToAI(
        SampleActivity sample,
        string stampId,
        int processId,
        DateTimeOffset sessionId,
        Guid dataCube,
        string machineName)
    {
        ArtifactLocationProperties traceLocation = sample.ToArtifactLocationProperties(stampId, processId, sessionId, dataCube, machineName);
        EventTelemetry eventTelemetry = new(ServiceProfilerSample);
        eventTelemetry.Properties.Add(ServiceProfilerContent, traceLocation.ToString());
        eventTelemetry.Properties.Add(ServiceProfilerVersion, "v2");
        eventTelemetry.Properties.Add(RequestId, sample.RequestId);
        eventTelemetry.Context.Cloud.RoleInstance = sample.RoleInstance;
        eventTelemetry.Context.Cloud.RoleName ??= CloudRoleName;
        eventTelemetry.Context.Operation.Name = sample.OperationName;
        eventTelemetry.Context.Operation.Id = sample.OperationId;

        SendCustomEventToAI(eventTelemetry);
    }

    protected virtual void SendCustomEventToAI(EventTelemetry telemetry)
    {
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            bool arePropertiesSerialized = _serializer.TrySerialize(telemetry.Properties, out string serializedProperties);
            if (arePropertiesSerialized)
            {
                _logger.LogTrace("[{0:O}] Sending custom event to AI: " + Environment.NewLine + "{1}." + Environment.NewLine + "iKey: {2}.",
                                DateTime.Now,
                                serializedProperties,
                                _context.AppInsightsInstrumentationKey);
            }
        }

        _telemetryClient.TrackEvent(telemetry);
    }

    protected ISerializationProvider _serializer { get; }
    private readonly ILogger _logger;
    private readonly IServiceProfilerContext _context;
    private readonly IResourceUsageSource _resourceUsage;
    private readonly TelemetryClient _telemetryClient;
    private string _roleNameCache;

    private const string ServiceProfilerSample = "ServiceProfilerSample";
    private const string ServiceProfilerContent = "ServiceProfilerContent";
    private const string ServiceProfilerVersion = "ServiceProfilerVersion";
    private const string RequestId = "RequestId";

    // Index event fields
    private const string ServiceProfilerIndex = "ServiceProfilerIndex";
    private const string FileId = "FileId";
    private const string StampId = "StampId";
    private const string DataCube = "DataCube";
    private const string EtlFileSessionId = "EtlFileSessionId";
    private const string MachineName = "MachineName";
    private const string ProcessId = "ProcessId";
    private const string Source = "Source";
    private const string OperatingSystem = "OperatingSystem";
    private const string AverageCPUUsage = "AverageCPUUsage";
    private const string AverageMemoryUsage = "AverageMemoryUsage";
}
