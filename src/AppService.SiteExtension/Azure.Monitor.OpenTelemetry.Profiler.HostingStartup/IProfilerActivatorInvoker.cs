// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.DependencyInjection;

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

/// <summary>
/// Enables the profiler that matches a detected <see cref="TelemetryStack"/> by invoking the corresponding
/// per-stack activator. Abstracted so the router can be unit-tested without the real activator assemblies
/// (and their full profiler closures) being present.
/// </summary>
internal interface IProfilerActivatorInvoker
{
    /// <summary>
    /// Activates the profiler for <paramref name="stack"/> against <paramref name="services"/>. Does nothing
    /// for <see cref="TelemetryStack.None"/>.
    /// </summary>
    void Invoke(TelemetryStack stack, IServiceCollection services);
}
