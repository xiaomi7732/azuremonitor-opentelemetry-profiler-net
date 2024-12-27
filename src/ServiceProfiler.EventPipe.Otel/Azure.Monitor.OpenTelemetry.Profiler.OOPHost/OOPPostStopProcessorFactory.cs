// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

namespace Azure.Monitor.OpenTelemetry.Profiler.OOPHost;

internal class OOPPostStopProcessorFactory : IPostStopProcessorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public OOPPostStopProcessorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public IPostStopProcessor Create()
    {
        return ActivatorUtilities.CreateInstance<OOPPostStopProcessor>(_serviceProvider);
    }
}
