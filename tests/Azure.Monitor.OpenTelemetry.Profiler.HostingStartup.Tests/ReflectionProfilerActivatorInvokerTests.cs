// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartupTests;

public class ReflectionProfilerActivatorInvokerTests
{
    [Fact]
    internal void TryGetActivator_OpenTelemetry_MapsToOtelActivator()
    {
        bool resolved = ReflectionProfilerActivatorInvoker.TryGetActivator(
            TelemetryStack.OpenTelemetry, out string assembly, out string type);

        Assert.True(resolved);
        Assert.Equal("Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.OpenTelemetryActivator", assembly);
        Assert.Equal(assembly + ".OpenTelemetryProfilerActivator", type);
    }

    [Fact]
    internal void TryGetActivator_LegacyApplicationInsights_MapsToClassicActivator()
    {
        bool resolved = ReflectionProfilerActivatorInvoker.TryGetActivator(
            TelemetryStack.LegacyApplicationInsights, out string assembly, out string type);

        Assert.True(resolved);
        Assert.Equal("Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.ClassicActivator", assembly);
        Assert.Equal(assembly + ".ClassicProfilerActivator", type);
    }

    [Fact]
    internal void TryGetActivator_None_ReturnsFalse()
    {
        bool resolved = ReflectionProfilerActivatorInvoker.TryGetActivator(
            TelemetryStack.None, out string assembly, out string type);

        Assert.False(resolved);
        Assert.Equal(string.Empty, assembly);
        Assert.Equal(string.Empty, type);
    }

    [Fact]
    internal void Invoke_None_DoesNothing()
    {
        // No activator to load, so this must not attempt an assembly load or throw.
        new ReflectionProfilerActivatorInvoker().Invoke(TelemetryStack.None, new ServiceCollection());
    }
}
