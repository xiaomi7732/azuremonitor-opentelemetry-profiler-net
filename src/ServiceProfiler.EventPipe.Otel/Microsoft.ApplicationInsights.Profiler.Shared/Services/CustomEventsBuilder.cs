using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ServiceProfiler.Contract;
using Microsoft.ServiceProfiler.Contract.Agent;
using Microsoft.ServiceProfiler.Orchestration;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal class CustomEventsBuilder : ICustomEventsBuilder
{
    private static readonly int _processId = CurrentProcessUtilities.GetId();
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly IRoleNameSource _roleNameSource;
    private readonly IResourceUsageSource _resourceUsageSource;
    private string? _roleNameCache;

    public CustomEventsBuilder(
        IServiceProfilerContext serviceProfilerContext,
        IRoleNameSource roleNameSource,
        IResourceUsageSource resourceUsageSource)
    {
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _roleNameSource = roleNameSource ?? throw new ArgumentNullException(nameof(roleNameSource));
        _resourceUsageSource = resourceUsageSource;
    }

    public ServiceProfilerIndex CreateServiceProfilerIndex(string fileId, string stampId, DateTimeOffset sessionId, Guid appId, IProfilerSource profilerSource)
    {
        return new ServiceProfilerIndex()
        {
            Timestamp = sessionId.UtcDateTime,
            FileId = fileId,
            StampId = stampId,
            DataCube = StoragePathContract.GetDataCubeNameString(appId),
            EtlFileSessionId = TimestampContract.TimestampToString(sessionId),
            MachineName = _serviceProfilerContext.MachineName,
            ProcessId = _processId,
            Source = profilerSource.Source,
            OperatingSystem = Environment.OSVersion.VersionString,
            AverageCPUUsage = _resourceUsageSource.GetAverageCPUUsage(),
            AverageMemoryUsage = _resourceUsageSource.GetAverageMemoryUsage(),
            CloudRoleName = _roleNameCache ??= _roleNameSource.CloudRoleName
        };
    }

    public IEnumerable<ServiceProfilerSample> CreateServiceProfilerSamples(IReadOnlyCollection<SampleActivity> samples, string stampId, DateTimeOffset sessionId, Guid appId)
    {
        foreach (SampleActivity sample in samples)
        {
            yield return CreateServiceProfilerSample(sample, stampId, sessionId, appId);
        }
    }

    private ServiceProfilerSample CreateServiceProfilerSample(SampleActivity sample, string stampId, DateTimeOffset sessionId, Guid appId)
    {
        ArtifactLocationProperties traceLocation = sample.ToArtifactLocationProperties(stampId, _processId, sessionId, appId, _serviceProfilerContext.MachineName);
        return new ServiceProfilerSample()
        {
            Timestamp = sample.StartTimeUtc.UtcDateTime,
            ServiceProfilerContent = traceLocation.ToString(),
            ServiceProfilerVersion = "v2",
            RequestId = sample.RequestId,
            RoleInstance = sample.RoleInstance ?? _serviceProfilerContext.MachineName,
            RoleName = _roleNameCache ??= _roleNameSource.CloudRoleName,
            OperationId = sample.OperationId,
            OperationName = sample.OperationName,
        };
    }
}