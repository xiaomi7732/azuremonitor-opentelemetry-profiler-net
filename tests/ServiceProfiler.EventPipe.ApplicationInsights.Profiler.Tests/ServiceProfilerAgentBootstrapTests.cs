// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Shared;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ServiceProfiler.Orchestration;
using Moq;
using Xunit;

namespace ServiceProfiler.EventPipe.Client.Tests;

public class ServiceProfilerAgentBootstrapTests
{
    [Fact]
    public async Task ActivateAsync_NullConnectionString_LogsNotSetMessage()
    {
        (string? logged, bool running) = await RunBootstrapAsync(connectionStringValue: null);

        Assert.False(running);
        Assert.Contains("No connection string is set", logged);
    }

    [Fact]
    public async Task ActivateAsync_EmptyConnectionString_LogsEmptyMessage()
    {
        (string? logged, bool running) = await RunBootstrapAsync(connectionStringValue: "   ");

        Assert.False(running);
        Assert.Contains("connection string is empty", logged);
    }

    [Fact]
    public async Task ActivateAsync_MalformedConnectionString_LogsMalformedMessage()
    {
        // A non-empty value that fails to parse leaves the parsed ConnectionString null.
        (string? logged, bool running) = await RunBootstrapAsync(connectionStringValue: "this-is-not-a-valid-connection-string");

        Assert.False(running);
        Assert.Contains("malformed", logged);
    }

    [Theory]
    [InlineData("InstrumentationKey=")]
    [InlineData("InstrumentationKey=   ")]
    [InlineData("IngestionEndpoint=https://example.com/;InstrumentationKey=")]
    [InlineData("InstrumentationKey=00000000-0000-0000-0000-000000000001;InstrumentationKey=")]
    public async Task ActivateAsync_EmptyInstrumentationKey_LogsInstrumentationKeyMessage(string connectionStringValue)
    {
        // An explicitly empty instrumentation key fails to parse (ConnectionString stays null),
        // but should surface the instrumentation-key-specific message rather than the generic one.
        (string? logged, bool running) = await RunBootstrapAsync(connectionStringValue);

        Assert.False(running);
        Assert.Contains("Instrumentation key", logged);
    }

    private static async Task<(string? loggedError, bool running)> RunBootstrapAsync(string? connectionStringValue)
    {
        using BootstrapState bootstrapState = new();

        Mock<IServiceProfilerContext> contextMock = new();
        contextMock.SetupGet(c => c.ConnectionStringValue).Returns(connectionStringValue);
        // ConnectionString is left at its default (null), simulating an unset or unparsable value.

        Mock<IOrchestrator> orchestratorMock = new();

        IOptions<UserConfigurationBase> options = Options.Create<UserConfigurationBase>(new TestUserConfiguration
        {
            IsDisabled = false,
            IsSkipCompatibilityTest = true,
        });

        Mock<ICompatibilityUtilityFactory> compatibilityFactoryMock = new();

        Mock<ISerializationProvider> serializerMock = new();
        string? ignored;
        serializerMock.Setup(s => s.TrySerialize(It.IsAny<UserConfigurationBase>(), out ignored)).Returns(false);

        Mock<IEventPipeEnvironmentCheckService> environmentCheckMock = new();
        environmentCheckMock.Setup(e => e.IsEnvironmentSuitable()).Returns(true);

        TestLogger logger = new();

        ServiceProfilerAgentBootstrap bootstrap = new(
            bootstrapState,
            contextMock.Object,
            orchestratorMock.Object,
            options,
            compatibilityFactoryMock.Object,
            serializerMock.Object,
            environmentCheckMock.Object,
            logger);

        await bootstrap.ActivateAsync(CancellationToken.None);

        orchestratorMock.Verify(o => o.StartAsync(It.IsAny<CancellationToken>()), Times.Never);

        bool running = bootstrapState.IsProfilerRunning(CancellationToken.None);
        return (logger.LastError, running);
    }

    private sealed class TestUserConfiguration : UserConfigurationBase
    {
    }

    private sealed class TestLogger : ILogger<ServiceProfilerAgentBootstrap>
    {
        public string? LastError { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                LastError = formatter(state, exception);
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
