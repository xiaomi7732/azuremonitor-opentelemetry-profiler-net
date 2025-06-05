using System;
using System.IO;

namespace Microsoft.ApplicationInsights.Profiler.Core.Utilities
{
    internal interface IProfilerCoreAssemblyInfo
    {
        Version Version { get; }
        DirectoryInfo Directory { get; }
    }
}