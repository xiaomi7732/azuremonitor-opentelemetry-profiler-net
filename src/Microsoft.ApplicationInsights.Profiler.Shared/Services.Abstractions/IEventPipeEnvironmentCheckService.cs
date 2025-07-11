namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface IEventPipeEnvironmentCheckService
{
    /// <summary>
    /// Checks if the environment is suitable for EventPipe to run.
    /// https://learn.microsoft.com/dotnet/core/tools/dotnet-environment-variables#dotnet_enablediagnostics
    /// </summary>
    /// <returns>True if the environment is suitable, otherwise false.</returns>
    bool IsEnvironmentSuitable();
}