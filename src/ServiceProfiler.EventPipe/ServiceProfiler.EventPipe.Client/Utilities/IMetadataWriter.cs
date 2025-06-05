using Microsoft.ServiceProfiler.Contract;
using System.Collections.Generic;

namespace Microsoft.ApplicationInsights.Profiler.Core.Utilities
{
    internal interface IMetadataWriter
    {
        void Write(string filePath, IEnumerable<ArtifactLocationProperties> locationProperties);
    }
}
