using Azure.Core;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.Auth;
using Microsoft.Extensions.Logging;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal class AuthTokenProvider(ILogger<AuthTokenProvider> logger) : IAuthTokenProvider
{
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public bool IsAADAuthenticateEnabled => false;

    public Task<AccessToken> GetTokenAsync(CancellationToken cancellationToken)
    {
        _logger.LogWarning("{name} is not implemented.", nameof(AuthTokenProvider));
        return Task.FromResult(default(AccessToken));
    }
}