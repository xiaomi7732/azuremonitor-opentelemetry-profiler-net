//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Agent.Exceptions;
using Microsoft.ServiceProfiler.Contract.Agent.Profiler;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Orchestrations;
internal abstract class RemoteSettingsServiceBase
{
    private readonly ILogger _logger;
    private readonly TaskCompletionSource<bool> _taskCompletionSource;
    private readonly UserConfigurationBase _userConfiguration;
    private IProfilerFrontendClientFactory _frontendClientFactory;

    public SettingsContract CurrentSettings { get; private set; } = null;

    public event Action<SettingsContract> SettingsUpdated;

    public RemoteSettingsServiceBase(
        IProfilerFrontendClientFactory frontendClientFactory,
        IOptions<UserConfigurationBase> userConfigurationOptions,
        ILogger<RemoteSettingsServiceBase> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userConfiguration = userConfigurationOptions.Value ?? throw new ArgumentNullException(nameof(userConfigurationOptions));
        _frontendClientFactory = frontendClientFactory ?? throw new ArgumentNullException(nameof(frontendClientFactory));
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
            _logger.LogTrace("Initial remote settings fetched within given time: {0}.", timeout);
            return true;
        }
        else
        {
            _logger.LogDebug("Remote settings fetch timed out. Timeout settings: {0}", timeout);
            return false;
        }
    }

    protected abstract IDisposable EnterInternalZone();

    private async Task FetchRemoteSettingsAsync(CancellationToken cancellationToken)
    {
        using IDisposable internalZoneHandler = EnterInternalZone();
        try
        {
            if (_frontendClientFactory != null)
            {
                _logger.LogTrace("Fetching remote settings.");
                SettingsContract newContract = await _frontendClientFactory.CreateProfilerFrontendClient().GetProfilerSettingsAsync(cancellationToken).ConfigureAwait(false);
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
            else
            {
                _logger.LogTrace("{0} is null. Indicating it is disposed.", nameof(_frontendClientFactory));
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
        }
    }

    private void OnSettingsUpdated(SettingsContract settingsContract)
    {
        CurrentSettings = settingsContract;
        SettingsUpdated?.Invoke(settingsContract);
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
}
