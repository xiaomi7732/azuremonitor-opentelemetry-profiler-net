using Azure.Core;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal class AuthTokenProvider(
    IOptions<ServiceProfilerOptions> serviceProfilerOptions,
    ILogger<AuthTokenProvider> logger) : IAuthTokenProvider
{
    private readonly ServiceProfilerOptions _serviceProfilerOptions = serviceProfilerOptions?.Value ?? throw new ArgumentNullException(nameof(ServiceProfilerOptions));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public bool IsAADAuthenticateEnabled => _serviceProfilerOptions.Credential is not null;

    public async Task<AccessToken> GetTokenAsync(CancellationToken cancellationToken)
    {
        TokenCredential? tokenCredential = _serviceProfilerOptions.Credential;
        if (tokenCredential is null)
        {
            throw new InvalidOperationException($"Credential is not provided. How does it pass the check of {nameof(IsAADAuthenticateEnabled)}?");
        }
        TokenRequestContext tokenRequestContext = new(scopes: ["https://monitor.azure.com/"]);
        AccessToken accessToken = await tokenCredential.GetTokenAsync(tokenRequestContext, cancellationToken).ConfigureAwait(false);
        _logger.LogTrace("Access token: {token}", accessToken.Token);
        return accessToken;
    }
}