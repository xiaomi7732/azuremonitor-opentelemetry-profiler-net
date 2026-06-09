// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Monitor.OpenTelemetry.Profiler;
using Azure.Monitor.OpenTelemetry.Profiler.Core;
using Microsoft.ApplicationInsights.Profiler.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Runtime.InteropServices;

namespace Azure.Monitor.OpenTelemetry.Profiler.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAzureMonitorProfiler_RegistersHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAzureMonitorProfiler();

        if (PlatformSupport.IsSupportedPlatform())
        {
            Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
        }
        else
        {
            Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
        }
    }

    [Fact]
    public void AddAzureMonitorProfiler_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        IServiceCollection result = services.AddAzureMonitorProfiler();

        Assert.Same(services, result);
    }

    [Fact]
    public void AddAzureMonitorProfiler_PreventsDoubleRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddAzureMonitorProfiler();
        int countAfterFirst = services.Count;

        services.AddAzureMonitorProfiler();
        int countAfterSecond = services.Count;

        Assert.Equal(countAfterFirst, countAfterSecond);
    }

    [Fact]
    public void AddAzureMonitorProfiler_AcceptsConfigureAction()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Should not throw when a configure action is provided
        services.AddAzureMonitorProfiler(opt => { });

        if (PlatformSupport.IsSupportedPlatform())
        {
            Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
        }
    }

    [Fact]
    public void AddAzureMonitorProfiler_RegistersServicesWithoutOpenTelemetryBuilder()
    {
        // Key scenario: user calls AddAzureMonitorProfiler directly on IServiceCollection
        // without calling AddOpenTelemetry() first (e.g., App Insights 3.x scenario)
        var services = new ServiceCollection();
        services.AddLogging();

        // This should succeed without requiring IOpenTelemetryBuilder
        IServiceCollection result = services.AddAzureMonitorProfiler();

        Assert.Same(services, result);
        if (PlatformSupport.IsSupportedPlatform())
        {
            Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
        }
    }

    [Fact]
    public void IsSupportedPlatform_ReturnsTrueOnWindowsOrLinux()
    {
        bool result = PlatformSupport.IsSupportedPlatform();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.True(result);
        }
        else
        {
            Assert.False(result);
        }
    }
}
