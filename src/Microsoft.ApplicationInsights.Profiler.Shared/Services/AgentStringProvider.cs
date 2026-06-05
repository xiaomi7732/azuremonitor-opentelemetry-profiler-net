using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using System.Reflection;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

/// <summary>
/// A generic implementation of <see cref="IAgentStringProvider"/> that derives the agent string
/// from the assembly containing <typeparamref name="T"/>. This eliminates the need to duplicate
/// the provider in each head project — simply register it with a type from the target assembly.
/// </summary>
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
