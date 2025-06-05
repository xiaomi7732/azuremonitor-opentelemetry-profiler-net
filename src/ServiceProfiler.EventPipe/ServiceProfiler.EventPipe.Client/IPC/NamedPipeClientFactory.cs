using System;
using Microsoft.ApplicationInsights.Profiler.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.ApplicationInsights.Profiler.Core.IPC
{
    internal class NamedPipeClientFactory : INamedPipeClientFactory
    {
        private readonly NamedPipeOptions _options;
        private readonly IPayloadSerializer _payloadSerializer;
        private readonly ILoggerFactory _loggerFactory;

        public NamedPipeClientFactory(IOptions<UserConfiguration> userConfiguration, IPayloadSerializer payloadSerializer, ILoggerFactory loggerFactory)
        {
            _options = userConfiguration?.Value?.NamedPipe ?? throw new ArgumentNullException(nameof(userConfiguration));
            _payloadSerializer = payloadSerializer ?? throw new ArgumentNullException(nameof(payloadSerializer));
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        }

        public INamedPipeClientService CreateNamedPipeService() =>
            new DuplexNamedPipeService(_options, _payloadSerializer, _loggerFactory.CreateLogger<DuplexNamedPipeService>());
    }
}
