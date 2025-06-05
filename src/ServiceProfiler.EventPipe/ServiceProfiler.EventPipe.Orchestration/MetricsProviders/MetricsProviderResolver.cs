//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ServiceProfiler.Orchestration.MetricsProviders;

namespace Microsoft.ApplicationInsights.Profiler.Core.Orchestration.MetricsProviders;

internal class MetricsProviderResolver : IMetricsProviderResolver<MetricsProviderCategory>
{
    private readonly IServiceProvider _serviceProvider;
    public MetricsProviderResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public IMetricsProvider Resolve(MetricsProviderCategory category)
    {
        switch (category)
        {
            case MetricsProviderCategory.CPU:
                return _serviceProvider.GetRequiredService<ProcessInfoCPUMetricsProvider>();
            case MetricsProviderCategory.Memory:
                return _serviceProvider.GetRequiredService<MemInfoFileMemoryMetricsProvider>();
            default:
                throw new ArgumentOutOfRangeException(nameof(category), $"Unsupported metrics provider category: {category}");
        }
    }
}
