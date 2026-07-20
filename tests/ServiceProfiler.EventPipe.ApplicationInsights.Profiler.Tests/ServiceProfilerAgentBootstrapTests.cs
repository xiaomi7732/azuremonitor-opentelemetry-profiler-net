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
    public async Task ActivateAsync_NotConfigured_LogsNotSetMessage()
    {
        (string? logged, bool running) = await RunBootstrapAsync(ConnectionStringValidationResult.NotConfigured);

        Assert.False(running);
        Assert.Contains("No connection string is set", logged);
    }

    [Fact]
    public async Task ActivateAsync_Empty_LogsEmptyMessage()
    {
        (string? logged, bool running) = await RunBootstrapAsync(ConnectionStringValidationResult.Empty);

        Assert.False(running);
        Assert.Contains("connection string is empty", logged);
    }

    [Fact]
    public async Task ActivateAsync_Malformed_LogsMalformedMessage()
    {
        (string? logged, bool running) = await RunBootstrapAsync(ConnectionStringValidationResult.Malformed);

        Assert.False(running);
        Assert.Contains("malformed", logged);
    }

    [Fact]
    public async Task ActivateAsync_InvalidInstrumentationKey_LogsInstrumentationKeyMessage()
    {
        (string? logged, bool running) = await RunBootstrapAsync(ConnectionStringValidationResult.InvalidInstrumentationKey);

        Assert.False(running);
        Assert.Contains("Instrumentation key", logged);
    }

    private static async Task<(string? loggedError, bool running)> RunBootstrapAsync(ConnectionStringValidationResult validation)
    {
        using BootstrapState bootstrapState = new();

        Mock<IServiceProfilerContext> contextMock = new();
        contextMock.SetupGet(c => c.ConnectionStringValidation).Returns(validation);

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
        return (logger.LastMessage, running);
    }

    private sealed class TestUserConfiguration : UserConfigurationBase
    {
    }

    private sealed class TestLogger : ILogger<ServiceProfilerAgentBootstrap>
    {
        // Captures the last logged message regardless of level. The connection-string reason is logged
        // by the bootstrap at Debug (the authoritative Error-level diagnostic is emitted earlier by
        // ProfilerBackgroundService); these tests verify the bootstrap still surfaces the reason and
        // disables profiling for each validation result.
        public string? LastMessage { get; private set; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LastMessage = formatter(state, exception);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
