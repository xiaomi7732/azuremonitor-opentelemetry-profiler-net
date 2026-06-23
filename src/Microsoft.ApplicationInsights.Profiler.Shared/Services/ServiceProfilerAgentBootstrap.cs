// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Orchestration;

namespace Microsoft.ApplicationInsights.Profiler.Shared;

internal class ServiceProfilerAgentBootstrap : IServiceProfilerAgentBootstrap
{
    private readonly BootstrapState _bootstrapState;
    private readonly IServiceProfilerContext _serviceProfilerContext;
    private readonly IOrchestrator _orchestrator;
    private readonly UserConfigurationBase _userConfiguration;
    private readonly ICompatibilityUtilityFactory _compatibilityUtilityFactory;
    private readonly ISerializationProvider _serializer;
    private readonly IEventPipeEnvironmentCheckService _eventPipeEnvironmentCheckService;
    private readonly ILogger _logger;

    public ServiceProfilerAgentBootstrap(
        BootstrapState bootstrapState,
        IServiceProfilerContext serviceProfilerContext,
        IOrchestrator orchestrator,
        IOptions<UserConfigurationBase> userConfiguration,
        ICompatibilityUtilityFactory compatibilityUtilityFactory,
        ISerializationProvider serializer,
        IEventPipeEnvironmentCheckService eventPipeEnvironmentCheckService,
        ILogger<ServiceProfilerAgentBootstrap> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _userConfiguration = userConfiguration?.Value ?? throw new ArgumentNullException(nameof(userConfiguration));

        _compatibilityUtilityFactory = compatibilityUtilityFactory ?? throw new ArgumentNullException(nameof(compatibilityUtilityFactory));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _eventPipeEnvironmentCheckService = eventPipeEnvironmentCheckService ?? throw new ArgumentNullException(nameof(eventPipeEnvironmentCheckService));
        _bootstrapState = bootstrapState ?? throw new ArgumentNullException(nameof(bootstrapState));
        _serviceProfilerContext = serviceProfilerContext ?? throw new ArgumentNullException(nameof(serviceProfilerContext));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
    }

    public async Task ActivateAsync(CancellationToken cancellationToken)
    {
        if (_serializer.TrySerialize(_userConfiguration, out string? serializedUserConfiguration))
        {
            _logger.LogDebug("User Settings:{eol} {details}", Environment.NewLine, serializedUserConfiguration);
        }

        if (_userConfiguration.IsDisabled)
        {
            _logger.LogInformation("Service Profiler is disabled by the configuration.");
            Activated(false);
            return;
        }

        _logger.LogTrace("Starting service profiler from application builder.");
        (bool compatible, string reason) = _userConfiguration.IsSkipCompatibilityTest ? (true, "Skipped the compatibility test by settings.") : _compatibilityUtilityFactory.Create().IsCompatible();

        if (!string.IsNullOrEmpty(reason)) { _logger.LogDebug(reason); }

        if (!compatible)
        {
            _logger.LogError("Compatibility test failed. Reason: {reason}" + Environment.NewLine +
                "Bypass the compatibility test by setting environment variable of ServiceProfiler__IsSkipCompatibilityTest to true.", reason);
            Activated(false);
            return;
        }

        if (!_eventPipeEnvironmentCheckService.IsEnvironmentSuitable())
        {
            _logger.LogError("Environment check failed. Profiler is disabled.");
            Activated(false);
            return;
        }

        try
        {
            // Diagnose the connection string state to provide an actionable error message.
            string? connectionStringError = GetConnectionStringConfigurationError();
            if (connectionStringError is not null)
            {
                _logger.LogError(connectionStringError + ProfilerWontStartSuffix);
                Activated(false);
                return;
            }

            _logger.LogInformation("Starting application insights profiler with connection string: {connectionString}", _serviceProfilerContext.ConnectionString);

            // Signal activation BEFORE starting orchestrator to avoid circular wait
            Activated(true);

            await _orchestrator.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
        {
            // Terminated.
            _logger.LogDebug("Profiler terminated by the user.");
        }
        catch (ArgumentNullException ex) when (string.Equals(ex.ParamName, "instrumentationKey", StringComparison.OrdinalIgnoreCase))
        {
            Debug.Fail("You hit the safety net! How could it escape the instrumentation key check?");
            _logger.LogError(NoConnectionStringMessage + ProfilerWontStartSuffix);
            Activated(false);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during profiler activation.");
            _logger.LogTrace(ex, "Full stack trace: {stackTrace}", ex.ToString());
            Activated(false); // Ensure activation event fires even on failure
            if (_userConfiguration.AllowsCrash)
            {
                throw;
            }
        }
    }

    private void Activated(bool isRunning)
    {
        _bootstrapState.SetProfilerRunning(isRunning);
    }

    private const string ProfilerWontStartSuffix = " Application Insights Profiler won't start.";
    private const string NoConnectionStringMessage = "No connection string is set.";
    private const string InstrumentationKeyMessage = "Instrumentation key is not set or malformed in the connection string.";

    /// <summary>
    /// Maps the connection string validation result to an actionable error message
    /// describing why the profiler cannot start, or <see langword="null"/> when it is valid.
    /// </summary>
    private string? GetConnectionStringConfigurationError() => _serviceProfilerContext.ConnectionStringValidation switch
    {
        ConnectionStringValidationResult.Valid => null,
        ConnectionStringValidationResult.NotConfigured => NoConnectionStringMessage,
        ConnectionStringValidationResult.Empty => "The connection string is empty.",
        ConnectionStringValidationResult.InvalidInstrumentationKey => InstrumentationKeyMessage,
        ConnectionStringValidationResult.Malformed => "The connection string is malformed and could not be parsed. Verify the connection string and that it contains a valid instrumentation key.",
        _ => "The connection string is invalid.",
    };
}
