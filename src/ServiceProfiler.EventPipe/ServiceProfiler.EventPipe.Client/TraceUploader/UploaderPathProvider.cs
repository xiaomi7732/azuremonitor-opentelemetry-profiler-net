using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Microsoft.ApplicationInsights.Profiler.Core.UploaderProxy
{
    internal class UploaderPathProvider : IUploaderPathProvider
    {
        private readonly IEnumerable<IPrioritizedUploaderLocator> _uploaderLocators;
        private readonly ILogger _logger;

        public UploaderPathProvider(
            IEnumerable<IPrioritizedUploaderLocator> uploaderLocators,
            ILogger<UploaderPathProvider> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _uploaderLocators = (uploaderLocators ?? throw new ArgumentNullException(nameof(uploaderLocators))).OrderBy(locator => locator.Priority);
        }

        public string GetUploaderFullPath()
        {
            string uploaderFullPath = null;
            foreach (IPrioritizedUploaderLocator uploaderLocator in _uploaderLocators)
            {
                uploaderFullPath = uploaderLocator.Locate();
                if (!string.IsNullOrEmpty(uploaderFullPath))
                {
                    break;
                }
            }

            if(string.IsNullOrEmpty(uploaderFullPath))
            {
                _logger.LogError("Uploader can't be located.");
            }

            return uploaderFullPath;
        }

        public bool TryGetUploaderFullPath(out string uploaderFullPath)
        {
            uploaderFullPath = GetUploaderFullPath();
            return !string.IsNullOrEmpty(uploaderFullPath);
        }
    }
}
