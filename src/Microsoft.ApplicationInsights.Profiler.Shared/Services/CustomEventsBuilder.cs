//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts.CustomEvents;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Contract;
using Microsoft.ServiceProfiler.Contract.Agent;
using Microsoft.ServiceProfiler.Orchestration;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal class CustomEventsBuilder : ICustomEventsBuilder
{
    private static readonly int _processId = CurrentProcessUtilities.GetId();
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly IRoleNameSource _roleNameSource;
    private readonly IRoleInstanceSource _roleInstanceSource;
    private readonly ILogger _logger;
    private string? _roleNameCache;
    private string? _roleInstanceCache;

    public CustomEventsBuilder(
        IServiceProfilerContext serviceProfilerContext,
        IRoleNameSource roleNameSource,
        IRoleInstanceSource roleInstanceSource,
        ILogger<CustomEventsBuilder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _roleNameSource = roleNameSource ?? throw new ArgumentNullException(nameof(roleNameSource));
        _roleInstanceSource = roleInstanceSource ?? throw new ArgumentNullException(nameof(roleInstanceSource));
    }

    public ServiceProfilerIndex CreateServiceProfilerIndex(string stampId, DateTimeOffset sessionId, Guid appId, Guid artifactId, IProfilerSource profilerSource, float averageCPUUsage, float averageMemoryUsage)
    {
        ServiceProfilerIndex result = new()
        {
            Timestamp = sessionId.UtcDateTime,
            StampId = stampId,
            DataCube = StoragePathContract.GetDataCubeNameString(appId),
            EtlFileSessionId = TimestampContract.TimestampToString(sessionId),
            ArtifactId = artifactId,
            ArtifactKind = ArtifactKind.Profile.ToString(),
            Extension = StoragePathContract.ExtensionFromArtifactKind(ArtifactKind.Profile),
            ProgrammingLanguage = ProgramLanguages.CSharp,
            Source = profilerSource.Source,
            OperatingSystem = Environment.OSVersion.VersionString,
            AverageCPUUsage = averageCPUUsage,
            AverageMemoryUsage = averageMemoryUsage,
            CloudRoleName = _roleNameCache ??= _roleNameSource.CloudRoleName
        };

        _logger.LogDebug("Service Profiler Index content: {content}", result);

        return result;
    }

    public IEnumerable<ServiceProfilerSample> CreateServiceProfilerSamples(IReadOnlyCollection<SampleActivity> samples, string stampId, DateTimeOffset sessionId, Guid appId, Guid artifactId)
    {
        foreach (SampleActivity sample in samples)
        {
            yield return CreateServiceProfilerSample(sample, stampId, sessionId, appId, artifactId);
        }
    }

    private ServiceProfilerSample CreateServiceProfilerSample(SampleActivity sample, string stampId, DateTimeOffset sessionId, Guid appId, Guid artifactId)
    {
        ArtifactReference artifactRef = new()
        {
            AppId = appId,
            ArtifactId = artifactId,
            Kind = ArtifactKind.Profile,
            Extension = StoragePathContract.ExtensionFromArtifactKind(ArtifactKind.Profile),
        };

        ArtifactLocationProperties traceLocation = new ArtifactLocationProperties(artifactRef, stampId)
            .WithActivity(_processId, sample.StartActivityIdPath, sample.StartTimeUtc, sample.StopTimeUtc);
        return new ServiceProfilerSample()
        {
            Timestamp = sample.StartTimeUtc.UtcDateTime,
            ServiceProfilerContent = traceLocation.ToString(),
            ServiceProfilerVersion = "v2",
            RequestId = sample.RequestId,
            RoleInstance = _roleInstanceCache ??= _roleInstanceSource.CloudRoleInstance,
            RoleName = _roleNameCache ??= _roleNameSource.CloudRoleName,
            OperationId = sample.OperationId,
            OperationName = sample.OperationName,
        };
    }
}