// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

namespace Azure.Monitor.OpenTelemetry.Profiler.OOPHost;

internal class OOPPostStopProcessor : IPostStopProcessor
{
    private readonly ISerializationProvider _serializationProvider;
    private readonly ILogger _logger;

    public OOPPostStopProcessor(
        ISerializationProvider serializationProvider,
        ILogger<OOPPostStopProcessor> logger)
    {
        _serializationProvider = serializationProvider ?? throw new ArgumentNullException(nameof(serializationProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    public Task PostStopProcessAsync(PostStopOptions state, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Not implemented. Entering {name}", nameof(PostStopProcessAsync));

        if(_serializationProvider.TrySerialize(state, out string? serializedState))
        {
            _logger.LogInformation("Current state: {newline} {stateObject}", Environment.NewLine, serializedState);
        }

        _logger.LogWarning("Not implemented. Leaving {name}", nameof(PostStopProcessAsync));
        return Task.CompletedTask;
    }
}