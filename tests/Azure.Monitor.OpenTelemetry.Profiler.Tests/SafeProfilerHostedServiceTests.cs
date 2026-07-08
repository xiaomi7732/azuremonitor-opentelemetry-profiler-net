// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Monitor.OpenTelemetry.Profiler.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Azure.Monitor.OpenTelemetry.Profiler.Tests;

public class SafeProfilerHostedServiceTests
{
    [Fact]
    public async Task StartAsync_WhenInnerFactoryThrows_DoesNotPropagate()
    {
        // Simulates the inner profiler service throwing while being constructed/resolved (e.g. an invalid
        // connection string, a backend client that cannot be created, or an incompatible dependency version
        // in the codeless scenario). The host must not be faulted.
        SafeProfilerHostedService service = new(
            () => throw new MissingMethodException("simulated construction failure"),
            NullLogger<SafeProfilerHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WhenInnerStartThrows_DoesNotPropagate()
    {
        Mock<IHostedService> inner = new();
        inner.Setup(s => s.StartAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("simulated start failure"));

        SafeProfilerHostedService service = new(() => inner.Object, NullLogger<SafeProfilerHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        // Inner failed to start; StopAsync must be a safe no-op.
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_WhenFactoryReturnsNull_DoesNothing()
    {
        SafeProfilerHostedService service = new(() => null, NullLogger<SafeProfilerHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HappyPath_StartsAndStopsInner()
    {
        Mock<IHostedService> inner = new();
        inner.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        inner.Setup(s => s.StopAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        SafeProfilerHostedService service = new(() => inner.Object, NullLogger<SafeProfilerHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        inner.Verify(s => s.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
        inner.Verify(s => s.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_WhenInnerStopThrows_DoesNotPropagate()
    {
        Mock<IHostedService> inner = new();
        inner.Setup(s => s.StartAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        inner.Setup(s => s.StopAsync(It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("simulated stop failure"));

        SafeProfilerHostedService service = new(() => inner.Object, NullLogger<SafeProfilerHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
    }
}
