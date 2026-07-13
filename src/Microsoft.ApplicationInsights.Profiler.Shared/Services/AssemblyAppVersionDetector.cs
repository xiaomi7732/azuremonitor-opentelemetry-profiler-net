//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System.Reflection;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

/// <summary>
/// Detects the application version from the entry assembly, preferring the
/// <see cref="AssemblyInformationalVersionAttribute"/> (the same source the Application Insights SDK
/// uses for <c>application_Version</c>) and falling back to the assembly <see cref="AssemblyName.Version"/>.
/// </summary>
internal sealed class AssemblyAppVersionDetector : IAppVersionDetector
{
    private readonly Assembly? _assembly;

    public AssemblyAppVersionDetector()
        : this(Assembly.GetEntryAssembly())
    {
    }

    // Exposed for testing with an explicit assembly.
    internal AssemblyAppVersionDetector(Assembly? assembly)
    {
        _assembly = assembly;
    }

    public string? GetAppVersion()
    {
        if (_assembly is null)
        {
            return null;
        }

        string? informationalVersion = _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return _assembly.GetName().Version?.ToString();
    }
}
