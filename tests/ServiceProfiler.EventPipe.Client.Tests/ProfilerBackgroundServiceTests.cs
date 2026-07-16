// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ServiceProfiler.EventPipe.Client.Tests;

public class ProfilerBackgroundServiceTests
{
    private sealed class TestUserConfiguration : UserConfigurationBase
    {
    }

    private static ProfilerBackgroundService CreateService(
        CapturingLogger logger,
        ConnectionStringValidationResult validation,
        bool isDisabled = false,
        bool throwOnValidation = false)
    {
        var bootstrap = new Mock<IServiceProfilerAgentBootstrap>();
        var context = new Mock<IServiceProfilerContext>();
        if (throwOnValidation)
        {
            context.SetupGet(c => c.ConnectionStringValidation).Throws(new InvalidOperationException("boom"));
        }
        else
        {
            context.SetupGet(c => c.ConnectionStringValidation).Returns(validation);
        }

        IOptions<UserConfigurationBase> options = Options.Create<UserConfigurationBase>(new TestUserConfiguration { IsDisabled = isDisabled });
        return new ProfilerBackgroundService(bootstrap.Object, context.Object, options, logger);
    }

    [Fact]
    public async Task InvalidConnectionString_LogsActionableErrorEarly()
    {
        (ConnectionStringValidationResult Validation, string Fragment)[] cases =
        {
            (ConnectionStringValidationResult.NotConfigured, "No connection string is set"),
            (ConnectionStringValidationResult.Empty, "connection string is empty"),
            (ConnectionStringValidationResult.InvalidInstrumentationKey, "Instrumentation key"),
            (ConnectionStringValidationResult.Malformed, "malformed"),
        };

        foreach ((ConnectionStringValidationResult validation, string expectedFragment) in cases)
        {
            var logger = new CapturingLogger();
            ProfilerBackgroundService service = CreateService(logger, validation);
            await service.StartAsync(CancellationToken.None);
            await service.StopAsync(CancellationToken.None);

            (LogLevel Level, string Message) error = Assert.Single(logger.Entries.Where(e => e.Level == LogLevel.Error));
            Assert.Contains(expectedFragment, error.Message);
            // Includes the actionable hint that the Azure Monitor exporter also needs a connection string.
            Assert.Contains("Azure Monitor exporter", error.Message);
        }
    }

    [Fact]
    public async Task ValidConnectionString_LogsNoError()
    {
        var logger = new CapturingLogger();
        ProfilerBackgroundService service = CreateService(logger, ConnectionStringValidationResult.Valid);
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task DisabledProfiler_LogsNoConnectionStringError()
    {
        // When the profiler is disabled by configuration, a missing connection string is irrelevant to
        // the profiler and must not produce an error (which would be misleading and could trip alerts).
        var logger = new CapturingLogger();
        ProfilerBackgroundService service = CreateService(logger, ConnectionStringValidationResult.NotConfigured, isDisabled: true);
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ContextValidationThrows_DoesNotFaultStartup()
    {
        var logger = new CapturingLogger();
        ProfilerBackgroundService service = CreateService(logger, ConnectionStringValidationResult.NotConfigured, throwOnValidation: true);

        // The early diagnostic must never fault host startup (the classic profiler has no
        // SafeProfilerHostedService wrapper).
        Exception ex = await Record.ExceptionAsync(() => service.StartAsync(CancellationToken.None));
        await service.StopAsync(CancellationToken.None);

        Assert.Null(ex);
    }

    private sealed class CapturingLogger : ILogger<ProfilerBackgroundService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
