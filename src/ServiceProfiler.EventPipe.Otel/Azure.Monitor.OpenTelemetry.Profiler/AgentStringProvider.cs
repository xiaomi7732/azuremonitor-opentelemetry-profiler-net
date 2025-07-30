using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using System.Reflection;

namespace Azure.Monitor.OpenTelemetry.Profiler;

/// <summary>
/// An implementation of <see cref="IAgentStringProvider"/> that provides the agent string for the profiler.
/// The agent string is constructed using the specified type's assembly name and version.
/// </summary>
/// <typeparam name="T">The type whose assembly information will be used to construct the agent string.</typeparam>
internal class AgentStringProvider<T> : IAgentStringProvider
{
    private static readonly string _agentString = CreateAgentString();

    private static string CreateAgentString()
    {
        Assembly assembly = typeof(T).Assembly;
        AssemblyName assemblyName = assembly.GetName();
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "unknown";

        return $"{assemblyName.Name}/{version}";
    }

    public string AgentString => _agentString;
}