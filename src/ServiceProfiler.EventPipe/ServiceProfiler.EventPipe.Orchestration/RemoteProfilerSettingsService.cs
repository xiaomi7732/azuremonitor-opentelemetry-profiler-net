//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Agent.Exceptions;
using Microsoft.ServiceProfiler.Agent.FrontendClient;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using Microsoft.ServiceProfiler.Orchestration;

namespace Microsoft.ApplicationInsights.Profiler.Core.Orchestration;

// TODO: Make this a hosted service running on the background.
internal sealed class RemoteProfilerSettingsService : IProfilerSettingsService
{
    private IProfilerFrontendClient _frontendClient;
    private readonly TaskCompletionSource<bool> _taskCompletionSource;
    private readonly UserConfiguration _userConfiguration;
    private readonly ILogger _logger;

    public event Action<SettingsContract> SettingsUpdated;
    public SettingsContract CurrentSettings { get; private set; } = null;

    public RemoteProfilerSettingsService(
        IProfilerFrontendClient frontendClient,
        IOptions<UserConfiguration> userConfigurationOptions,
        ILogger<RemoteProfilerSettingsService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userConfiguration = userConfigurationOptions.Value ?? throw new ArgumentNullException(nameof(userConfigurationOptions));
        _frontendClient = frontendClient ?? throw new ArgumentNullException(nameof(frontendClient));
        _taskCompletionSource = new TaskCompletionSource<bool>();
        // Bootstrap the fetching service.
        _ = Task.Run(() => StartAsync(_userConfiguration.ConfigurationUpdateFrequency, default));
    }

    public async Task<bool> WaitForInitializedAsync(TimeSpan timeout)
    {
        Task completed = await Task.WhenAny(Task.Delay(timeout), _taskCompletionSource.Task).ConfigureAwait(false);
        if (completed == _taskCompletionSource.Task)
        {
            // Initialize done.
            _logger.LogTrace("Initial remote settings fetched within given time: {timeout}.", timeout);
            return true;
        }
        else
        {
            _logger.LogDebug("Remote settings fetch timed out. Timeout settings: {timeout}", timeout);
            return false;
        }
    }

    private async Task StartAsync(TimeSpan frequency, CancellationToken cancellationToken)
    {
        if (frequency > TimeSpan.Zero)
        {
            while (true)
            {
                await FetchRemoteSettingsAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(frequency, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            _logger.LogWarning("Configuration update frequency can't be negative.");
        }
    }

    private async Task FetchRemoteSettingsAsync(CancellationToken cancellationToken)
    {
        SdkInternalOperationsMonitor.Enter();
        try
        {
            _logger.LogTrace("Fetching remote settings.");
            SettingsContract newContract = await _frontendClient.GetProfilerSettingsAsync(cancellationToken).ConfigureAwait(false);
            if (newContract != null)
            {
                _logger.LogTrace("Remote settings fetched.");
                // New settings coming.
                OnSettingsUpdated(newContract);
            }
            else
            {
                _logger.LogTrace("No settings contract fetched.");
            }
        }
        catch (InstrumentationKeyInvalidException ikie)
        {
            _logger.LogError(ikie.Message);
            _logger.LogTrace(ikie.ToString());
        }
        catch (Exception ex) when (string.Equals(ex.Message, "Invalid instrumentation key format or instrumentation key has been revoked.", StringComparison.Ordinal))
        {
            _logger.LogError(ex.Message);
            _logger.LogTrace(ex.ToString());
        }
#pragma warning disable CA1031 // Only to allow for getting the value for the next iteration. The exception will be logged.
        catch (Exception ex)
#pragma warning restore CA1031 // Only to allow for getting the value for the next iteration. The exception will be logged.
        {
            // Move on for the next iteration.
            _logger.LogDebug("Unexpected error contacting service profiler service endpoint for settings. Details: {0}", ex);
            _logger.LogTrace(ex.ToString());
        }
        finally
        {
            // Either way, initialization is done.
            _taskCompletionSource.TrySetResult(true);
            SdkInternalOperationsMonitor.Exit();
        }
    }

    private void OnSettingsUpdated(SettingsContract settingsContract)
    {
        CurrentSettings = settingsContract;
        SettingsUpdated?.Invoke(settingsContract);
    }
}
