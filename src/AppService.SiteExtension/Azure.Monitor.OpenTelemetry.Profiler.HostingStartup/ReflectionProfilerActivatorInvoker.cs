// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

/// <summary>
/// Activates a profiler by reflectively loading the per-stack activator assembly and invoking its
/// <c>public static void Enable(IServiceCollection)</c> entry point.
///
/// This is the seam that keeps the two profiler stacks isolated. The router (this assembly) has NO
/// compile-time reference to either profiler stack, so publishing it pulls neither closure. Each stack is
/// published into its own payload subfolder (<c>otel\</c> / <c>classic\</c>) with its own, self-consistent
/// dependency versions. The <c>StartupHook</c> detects the stack up front and scopes the
/// <see cref="System.Runtime.Loader.AssemblyLoadContext.Resolving"/> fallback to that stack's subfolder, so
/// the <see cref="Assembly.Load(AssemblyName)"/> below (and every transitive dependency it triggers) is
/// served from the chosen folder only. The other stack's folder is never on the probe path, so their
/// dependencies never unify.
///
/// The activator's <c>Enable</c> takes the app's <see cref="IServiceCollection"/> - a
/// <c>Microsoft.Extensions.DependencyInjection.Abstractions</c> type shared with (and owned by) the target
/// application - so the instance crosses the boundary with the correct identity via roll-forward.
/// </summary>
internal sealed class ReflectionProfilerActivatorInvoker : IProfilerActivatorInvoker
{
    private const string OpenTelemetryActivatorAssembly = "Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.OpenTelemetryActivator";
    private const string OpenTelemetryActivatorType = OpenTelemetryActivatorAssembly + ".OpenTelemetryProfilerActivator";

    private const string ClassicActivatorAssembly = "Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.ClassicActivator";
    private const string ClassicActivatorType = ClassicActivatorAssembly + ".ClassicProfilerActivator";

    private const string EnableMethodName = "Enable";

    public void Invoke(TelemetryStack stack, IServiceCollection services)
    {
        if (!TryGetActivator(stack, out string assemblyName, out string typeName))
        {
            return;
        }

        // Resolved by name from the stack's payload subfolder (the StartupHook scoped the resolver there).
        Assembly activatorAssembly = Assembly.Load(new AssemblyName(assemblyName));
        Type activatorType = activatorAssembly.GetType(typeName, throwOnError: true)!;

        MethodInfo enable = activatorType.GetMethod(
            EnableMethodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(IServiceCollection) },
            modifiers: null)
            ?? throw new MissingMethodException(typeName, EnableMethodName);

        enable.Invoke(null, new object[] { services });
    }

    /// <summary>
    /// Maps a stack to its activator assembly/type. Returns <see langword="false"/> for
    /// <see cref="TelemetryStack.None"/> (nothing to activate).
    /// </summary>
    internal static bool TryGetActivator(TelemetryStack stack, out string assemblyName, out string typeName)
    {
        switch (stack)
        {
            case TelemetryStack.OpenTelemetry:
                assemblyName = OpenTelemetryActivatorAssembly;
                typeName = OpenTelemetryActivatorType;
                return true;
            case TelemetryStack.LegacyApplicationInsights:
                assemblyName = ClassicActivatorAssembly;
                typeName = ClassicActivatorType;
                return true;
            default:
                assemblyName = string.Empty;
                typeName = string.Empty;
                return false;
        }
    }
}
