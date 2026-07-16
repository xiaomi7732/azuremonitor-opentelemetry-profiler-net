// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Xunit;

namespace ServiceProfiler.EventPipe.Client.Tests;

public class ConnectionStringDiagnosticsTests
{
    [Fact]
    public void Valid_ReturnsNull()
        => Assert.Null(ConnectionStringDiagnostics.GetConfigurationError(ConnectionStringValidationResult.Valid));

    [Fact]
    public void Invalid_ReturnsNonEmptyMessage()
    {
        ConnectionStringValidationResult[] invalid =
        {
            ConnectionStringValidationResult.NotConfigured,
            ConnectionStringValidationResult.Empty,
            ConnectionStringValidationResult.InvalidInstrumentationKey,
            ConnectionStringValidationResult.Malformed,
        };

        foreach (ConnectionStringValidationResult validation in invalid)
        {
            Assert.False(string.IsNullOrEmpty(ConnectionStringDiagnostics.GetConfigurationError(validation)));
        }
    }
}
