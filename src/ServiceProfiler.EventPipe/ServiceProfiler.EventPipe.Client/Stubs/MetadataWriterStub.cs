using Microsoft.ServiceProfiler.Contract;
using System.Collections.Generic;

namespace Microsoft.ApplicationInsights.Profiler.Core.Utilities
{
    internal class MetadataWriterStub : IMetadataWriter
    {
        public void Write(string filePath, IEnumerable<ArtifactLocationProperties> locationProperties)
        {
            // Do nothing in stub.
        }
    }
}
