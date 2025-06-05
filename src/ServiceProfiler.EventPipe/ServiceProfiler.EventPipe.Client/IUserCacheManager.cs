using System.IO;

namespace Microsoft.ApplicationInsights.Profiler.Core;

internal interface IUserCacheManager
{
    DirectoryInfo TempTraceDirectory { get; }
    DirectoryInfo UserCacheDirectory { get; }
    DirectoryInfo UploaderDirectory { get; }
}

