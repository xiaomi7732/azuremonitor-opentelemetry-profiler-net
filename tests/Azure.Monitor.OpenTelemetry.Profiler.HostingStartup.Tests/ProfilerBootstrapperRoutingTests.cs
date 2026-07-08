// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartupTests;

public class ProfilerBootstrapperRoutingTests
{
    private static (Mock<IWebHostBuilder> Builder, List<Action<IServiceCollection>> Captured) CreateBuilder()
    {
        List<Action<IServiceCollection>> captured = new();
        Mock<IWebHostBuilder> builder = new();
        builder
            .Setup(b => b.ConfigureServices(It.IsAny<Action<IServiceCollection>>()))
            .Callback<Action<IServiceCollection>>(captured.Add)
            .Returns(() => builder.Object);
        return (builder, captured);
    }

    [Fact]
    internal void Apply_WhenOpenTelemetry_RegistersProfilerServices()
    {
        (Mock<IWebHostBuilder> builder, List<Action<IServiceCollection>> captured) = CreateBuilder();

        ProfilerBootstrapper.Apply(builder.Object, TelemetryStack.OpenTelemetry);

        // Exactly one ConfigureServices registration, and running it must populate the container.
        Action<IServiceCollection> register = Assert.Single(captured);
        ServiceCollection services = new();
        register(services);
        Assert.NotEmpty(services);
    }

    [Fact]
    internal void Apply_WhenLegacyApplicationInsights_RegistersConfigureServices()
    {
        (Mock<IWebHostBuilder> builder, List<Action<IServiceCollection>> captured) = CreateBuilder();

        ProfilerBootstrapper.Apply(builder.Object, TelemetryStack.LegacyApplicationInsights);

        Assert.Single(captured);
    }

    [Fact]
    internal void Apply_WhenNone_DoesNotRegisterAnything()
    {
        (Mock<IWebHostBuilder> builder, List<Action<IServiceCollection>> captured) = CreateBuilder();

        ProfilerBootstrapper.Apply(builder.Object, TelemetryStack.None);

        Assert.Empty(captured);
        builder.Verify(b => b.ConfigureServices(It.IsAny<Action<IServiceCollection>>()), Times.Never);
    }

    [Fact]
    internal void Configure_DispatchesDetectedStack()
    {
        (Mock<IWebHostBuilder> builder, List<Action<IServiceCollection>> captured) = CreateBuilder();
        Mock<ITelemetryStackDetector> detector = new();
        detector.Setup(d => d.Detect()).Returns(TelemetryStack.OpenTelemetry);

        new ProfilerBootstrapper(detector.Object).Configure(builder.Object);

        Assert.Single(captured);
    }

    [Fact]
    internal void Configure_WhenDetectorThrows_DoesNotThrow()
    {
        (Mock<IWebHostBuilder> builder, List<Action<IServiceCollection>> _) = CreateBuilder();
        Mock<ITelemetryStackDetector> detector = new();
        detector.Setup(d => d.Detect()).Throws(new InvalidOperationException("boom"));

        // Bootstrap failures must never break host startup.
        new ProfilerBootstrapper(detector.Object).Configure(builder.Object);
    }

    [Fact]
    internal void Apply_WhenOpenTelemetryRegistrationThrows_DeferredCallbackDoesNotPropagate()
    {
        (Mock<IWebHostBuilder> builder, List<Action<IServiceCollection>> captured) = CreateBuilder();

        ProfilerBootstrapper.Apply(builder.Object, TelemetryStack.OpenTelemetry);
        Action<IServiceCollection> register = Assert.Single(captured);

        // A services collection whose enumeration throws simulates an incompatible-dependency failure
        // (e.g. a MissingMethodException from a version the profiler was compiled against but the app does
        // not provide) surfacing inside AddAzureMonitorProfiler. The deferred ConfigureServices callback -
        // which runs inside the app's host build - MUST swallow it, otherwise the host crashes.
        Mock<IServiceCollection> throwing = new();
        throwing.Setup(s => s.GetEnumerator()).Throws(new MissingMethodException("simulated version skew"));

        // Must not throw.
        register(throwing.Object);
    }
}
