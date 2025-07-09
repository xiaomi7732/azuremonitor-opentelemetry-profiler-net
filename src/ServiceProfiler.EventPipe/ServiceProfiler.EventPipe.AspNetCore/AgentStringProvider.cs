using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using System.Reflection;

namespace Microsoft.ApplicationInsights.Profiler.AspNetCore;

/// <summary>
/// An implementation of <see cref="IAgentStringProvider"/> that provides the agent string for the profiler.
/// The agent string is constructed using the executing assembly's name and version.
/// It is important to put this implementation in a header project to identify the agent correctly.
/// </summary>
internal class AgentStringProvider : IAgentStringProvider
{
    private static readonly string _agentString = CreateAgentString();

    private static string CreateAgentString()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        AssemblyName assemblyName = assembly.GetName();
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "unknown";

        return $"{assemblyName.Name}/{version}";
    }

    public string AgentString => _agentString;
}