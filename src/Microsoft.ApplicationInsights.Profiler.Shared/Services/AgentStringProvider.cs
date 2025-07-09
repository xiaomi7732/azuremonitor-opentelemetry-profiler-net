using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using System;
using System.Reflection;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal class AgentStringProvider : IAgentStringProvider
{
    private static readonly string _agentString = CreateAgentString();

    private static string CreateAgentString()
    {
        Assembly entryAssembly = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException("Entry assembly is not available.");
        string name = entryAssembly.GetName().FullName;
        string version = entryAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? entryAssembly.GetName().Version?.ToString() ?? "unknown";

        return $"{name}/{version}";
    }

    public string AgentString => _agentString;
}