// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Azure.Core;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel;
using Microsoft.Extensions.Logging;
using ServiceProfiler.Common.Utilities;

namespace Microsoft.ApplicationInsights.Profiler.Core.Utilities;

/// <summary>
/// A simple provider to supply an instance of <see cref="TelemetryConfiguration"/> from a given <see cref="ConnectionString"/>.
/// </summary>
internal sealed class TelemetryConfigurationBuilder
{
    private readonly ConnectionString _connectionString;
    private readonly TokenCredential? _tokenCredential;
    private readonly IEnumerable<ITelemetryInitializer> _telemetryInitializers;
    private readonly ILogger _logger;
    private readonly Action<TelemetryConfiguration>? _onTelemetryConfigurationCreated;

    public TelemetryConfigurationBuilder(
        ConnectionString connectionString,
        IEnumerable<ITelemetryInitializer> telemetryInitializers,
        ILogger<TelemetryConfigurationBuilder> logger,
        TokenCredential? tokenCredential = null,
        Action<TelemetryConfiguration>? onTelemetryConfigurationCreated = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

        _tokenCredential = tokenCredential;
        _telemetryInitializers = telemetryInitializers ?? [];
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _onTelemetryConfigurationCreated = onTelemetryConfigurationCreated;
    }

    /// <summary>
    /// Gets a <see cref="TelemetryConfiguration"/> with a lifespan that is the same as the factory.
    /// </summary>
    public TelemetryConfiguration Build()
    {
        ServerTelemetryChannel telemetryChannel = new();
        TelemetryConfiguration telemetryConfiguration = new()
        {
            ConnectionString = _connectionString.ToString(),
            TelemetryChannel = telemetryChannel,
        };

        _onTelemetryConfigurationCreated?.Invoke(telemetryConfiguration);

        if (_tokenCredential is not null)
        {
            telemetryConfiguration.SetAzureTokenCredential(_tokenCredential);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            telemetryChannel.TransmissionStatusEvent += OnTransmissionStatus;
        }

        foreach (ITelemetryInitializer telemetryInitializer in _telemetryInitializers)
        {
            telemetryConfiguration.TelemetryInitializers.Add(telemetryInitializer);
        }

        telemetryChannel.Initialize(telemetryConfiguration);

        _logger.LogDebug(
            "TelemetryConfiguration created. Connection string: {connectionString}, iKey: {iKey}, Channel Endpoint Address: {channelEndpoint}",
            telemetryConfiguration.ConnectionString,
            telemetryConfiguration.InstrumentationKey,
            telemetryConfiguration.TelemetryChannel.EndpointAddress);

        return telemetryConfiguration;
    }

    private void OnTransmissionStatus(object sender, TransmissionStatusEventArgs e)
    {
        if (sender is Transmission senderTransmission)
        {
            _logger.LogDebug("Transmission endpoint address: {transmissionEndpoint}", senderTransmission.EndpointAddress);
        }
        else
        {
            _logger.LogDebug("Sender is not of type Transmission.");
        }

        if (e.Response is not null)
        {
            _logger.LogDebug("Telemetry transmission duration: {duration}ms, status: {statusCode} - {statusDescription}, retry-after header: {retryAfterHeader}",
                e.ResponseDurationInMs,
                e.Response.StatusCode,
                e.Response.StatusDescription,
                e.Response.RetryAfterHeader);

            if (e.Response.StatusCode < 200 || e.Response.StatusCode >= 400)
            {
                _logger.LogError("Transmission failed with status code {statusCode} and description {statusDescription}", e.Response.StatusCode, e.Response.StatusDescription);
            }
        }
    }
}
