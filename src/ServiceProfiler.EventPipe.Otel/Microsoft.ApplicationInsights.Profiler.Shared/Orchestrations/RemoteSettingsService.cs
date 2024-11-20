using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using Microsoft.ServiceProfiler.Orchestration;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Orchestrations;

internal class RemoteSettingsService : IProfilerSettingsService
{
    private readonly ILogger _logger;

    public SettingsContract? CurrentSettings { get; }

    public event Action<SettingsContract?>? SettingsUpdated;

    public RemoteSettingsService(ILogger<RemoteSettingsService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogWarning("Getting remote settings is not implemented. Returns null.");
        CurrentSettings = null;
        SettingsUpdated?.Invoke(CurrentSettings);
    }

    public Task<bool> WaitForInitializedAsync(TimeSpan timeout)
    {
        return Task.FromResult(true);
    }
}