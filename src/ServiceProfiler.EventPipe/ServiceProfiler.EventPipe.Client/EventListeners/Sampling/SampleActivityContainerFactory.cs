using Microsoft.Extensions.Logging;
using System;

namespace Microsoft.ApplicationInsights.Profiler.Core.Sampling
{
    internal class SampleActivityContainerFactory
    {
        public SampleActivityContainerFactory(ILoggerFactory loggerFactory)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        private ILoggerFactory _loggerFactory;

        public SampleActivityContainer CreateNewInstance()
        {
            return new SampleActivityContainer(_loggerFactory.CreateLogger<SampleActivityContainer>());
        }
    }
}