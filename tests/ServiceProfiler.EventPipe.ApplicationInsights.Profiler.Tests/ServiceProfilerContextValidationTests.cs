// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ServiceProfiler.EventPipe.Client.Tests;

public class ServiceProfilerContextValidationTests
{
    [Fact]
    public void Validation_NullValue_ReturnsNotConfigured()
        => Assert.Equal(ConnectionStringValidationResult.NotConfigured, Validate(null));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validation_EmptyOrWhitespace_ReturnsEmpty(string connectionStringValue)
        => Assert.Equal(ConnectionStringValidationResult.Empty, Validate(connectionStringValue));

    [Theory]
    [InlineData("this-is-not-a-valid-connection-string")]
    [InlineData("Foo=bar;Baz=qux")]
    public void Validation_Unparsable_ReturnsMalformed(string connectionStringValue)
        => Assert.Equal(ConnectionStringValidationResult.Malformed, Validate(connectionStringValue));

    [Theory]
    [InlineData("InstrumentationKey=")]
    [InlineData("InstrumentationKey=   ")]
    [InlineData("IngestionEndpoint=https://example.com/;InstrumentationKey=")]
    [InlineData("InstrumentationKey=00000000-0000-0000-0000-000000000001;InstrumentationKey=")]
    public void Validation_EmptyInstrumentationKey_ReturnsInvalidInstrumentationKey(string connectionStringValue)
        => Assert.Equal(ConnectionStringValidationResult.InvalidInstrumentationKey, Validate(connectionStringValue));

    [Theory]
    [InlineData("InstrumentationKey=not-a-guid")]
    [InlineData("InstrumentationKey=123")]
    [InlineData("IngestionEndpoint=https://example.com/;InstrumentationKey=not-a-guid")]
    public void Validation_NonGuidInstrumentationKey_ReturnsInvalidInstrumentationKey(string connectionStringValue)
        => Assert.Equal(ConnectionStringValidationResult.InvalidInstrumentationKey, Validate(connectionStringValue));

    private static ConnectionStringValidationResult Validate(string? connectionStringValue)
    {
        Mock<IEndpointProvider> endpointProviderMock = new();
        endpointProviderMock.Setup(e => e.GetEndpoint()).Returns(new Uri("https://localhost"));

        // Every case under test is one the connection string parser rejects, so the parsed
        // ConnectionString is null. The context classifies it from the raw value internally.
        ServiceProfilerContext context = new(
            connectionString: null,
            connectionStringValue: connectionStringValue,
            endpointProviderMock.Object,
            NullLogger<ServiceProfilerContext>.Instance);

        return context.ConnectionStringValidation;
    }
}
