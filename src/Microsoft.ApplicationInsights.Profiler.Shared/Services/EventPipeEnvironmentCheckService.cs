using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal class EventPipeEnvironmentCheckService : IEventPipeEnvironmentCheckService
{
    private readonly ILogger _logger;

    public EventPipeEnvironmentCheckService(ILogger<EventPipeEnvironmentCheckService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsEnvironmentSuitable()
    {
        bool result = true;
        foreach (string item in DiagnosticsVariables.GetAllVariables())
        {
            if (Environment.GetEnvironmentVariable(item) == "0")
            {
                _logger.LogError("{variable} is set to 0. Profiler is disabled", item);
                result = false;
            }
        }

        return result;
    }
}   