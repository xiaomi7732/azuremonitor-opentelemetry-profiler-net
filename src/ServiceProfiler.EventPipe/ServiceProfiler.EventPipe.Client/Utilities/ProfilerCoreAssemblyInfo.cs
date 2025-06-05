using System;
using System.IO;
using System.Reflection;

namespace Microsoft.ApplicationInsights.Profiler.Core.Utilities
{
    internal sealed class ProfilerCoreAssemblyInfo : IProfilerCoreAssemblyInfo
    {
        private ProfilerCoreAssemblyInfo() { }
        public static ProfilerCoreAssemblyInfo Instance { get; } = new ProfilerCoreAssemblyInfo();

        Lazy<Assembly> _thisAssembly = new Lazy<Assembly>(() => typeof(ProfilerCoreAssemblyInfo).Assembly);

        public Version Version => _thisAssembly.Value.GetName().Version;

        public DirectoryInfo Directory => new DirectoryInfo(Path.GetDirectoryName(_thisAssembly.Value.Location));
    }
}