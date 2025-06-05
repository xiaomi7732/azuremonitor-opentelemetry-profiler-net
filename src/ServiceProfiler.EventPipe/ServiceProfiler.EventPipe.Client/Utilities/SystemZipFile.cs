using System.IO.Compression;

namespace Microsoft.ApplicationInsights.Profiler.Core.Utilities
{
    /// <summary>
    /// A simple implementation to <see cref="Microsoft.ApplicationInsights.Profiler.Core.Utilities.IZipFile" />.
    /// </summary>
    internal class SystemZipFile : IZipFile
    {
        /// <inheritdoc />
        public void ExtractToDirectory(string sourceArchiveFileName, string destinationDirectoryName) => ZipFile.ExtractToDirectory(sourceArchiveFileName, destinationDirectoryName);
    }
}
