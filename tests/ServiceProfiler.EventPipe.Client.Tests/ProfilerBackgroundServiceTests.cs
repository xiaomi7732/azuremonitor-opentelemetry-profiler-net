// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ServiceProfiler.EventPipe.Client.Tests;

public class ProfilerBackgroundServiceTests
{
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
            var bootstrap = new Mock<IServiceProfilerAgentBootstrap>();
            var context = new Mock<IServiceProfilerContext>();
            context.SetupGet(c => c.ConnectionStringValidation).Returns(validation);

            var service = new ProfilerBackgroundService(bootstrap.Object, context.Object, logger);
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
        var bootstrap = new Mock<IServiceProfilerAgentBootstrap>();
        var context = new Mock<IServiceProfilerContext>();
        context.SetupGet(c => c.ConnectionStringValidation).Returns(ConnectionStringValidationResult.Valid);

        var service = new ProfilerBackgroundService(bootstrap.Object, context.Object, logger);
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task ContextValidationThrows_DoesNotFaultStartup()
    {
        var logger = new CapturingLogger();
        var bootstrap = new Mock<IServiceProfilerAgentBootstrap>();
        var context = new Mock<IServiceProfilerContext>();
        context.SetupGet(c => c.ConnectionStringValidation).Throws(new InvalidOperationException("boom"));

        var service = new ProfilerBackgroundService(bootstrap.Object, context.Object, logger);

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
