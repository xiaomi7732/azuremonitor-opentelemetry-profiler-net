using System;
using Microsoft.ApplicationInsights.Profiler.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Core.UploaderProxy
{
    internal abstract class UploadLocatorBase : IPrioritizedUploaderLocator
    {
        protected const string TraceUploaderAssemblyName = "Microsoft.ApplicationInsights.Profiler.Uploader.dll";
        protected IFile FileService { get; }
        protected ILogger Logger { get; }

        public UploadLocatorBase(
            IFile fileService,
            ILogger<IPrioritizedUploaderLocator> logger)
        {
            FileService = fileService ?? throw new ArgumentNullException(nameof(fileService));
            Logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
        }

        public abstract int Priority { get; }

        public string Locate()
        {
            Logger.LogDebug("Locating uploader by locator. Priority: {priority}.", Priority);
            string uploaderFullPath = GetUploaderFullPath();

            if (string.IsNullOrEmpty(uploaderFullPath))
            {
                Logger.LogDebug("Uploader can't be located.");
                return null;
            }

            if (FileService.Exists(uploaderFullPath))
            {
                Logger.LogDebug("Uploader found: {filePath}", uploaderFullPath);
                return uploaderFullPath;
            }

            Logger.LogDebug("Uploader not found: {filePath}", uploaderFullPath);

            return null;
        }

        /// <summary>
        /// Gets uploader path. If the uploader path is not applicable to the locator, returns null.
        /// </summary>
        protected abstract string GetUploaderFullPath();
    }
}
