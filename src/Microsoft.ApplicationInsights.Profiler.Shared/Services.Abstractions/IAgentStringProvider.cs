namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

/// <summary>
/// Provides the agent string for the profiler.
/// </summary>
internal interface IAgentStringProvider
{
    /// <summary>
    /// Gets the agent string.
    /// </summary>
    /// <returns>The agent string.</returns>
    string AgentString { get; }
}
