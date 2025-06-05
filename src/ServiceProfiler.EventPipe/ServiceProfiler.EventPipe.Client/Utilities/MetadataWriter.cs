using Microsoft.ServiceProfiler.Contract;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Microsoft.ApplicationInsights.Profiler.Core.Utilities
{
    internal class MetadataWriter : IMetadataWriter
    {
        public void Write(string filePath, IEnumerable<ArtifactLocationProperties> locationProperties)
        {
            using (StreamWriter writer = new StreamWriter(filePath, true, Encoding.UTF8))
            {
                if (locationProperties!=null && locationProperties.Any())
                {
                    foreach (ArtifactLocationProperties line in locationProperties)
                    {
                        writer.WriteLine(line.ToString());
                    }
                }
                else
                {
                    writer.WriteLine("No metadata.");
                }
            }
        }
    }
}
