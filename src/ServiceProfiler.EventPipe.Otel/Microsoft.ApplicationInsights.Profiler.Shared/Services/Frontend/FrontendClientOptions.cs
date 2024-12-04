// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Net.Http;
using Azure.Core;
using Azure.Core.Pipeline;
using Microsoft.ServiceProfiler.HttpPipeline;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Frontend;

/// <summary>
/// Provides options to configure <see cref="Pipeline.HttpPipeline"/>
/// </summary>
internal class FrontendClientOptions : ClientOptions
{
    /// <summary>
    /// The scope exposed by your application for which the client application is requesting consent
    /// </summary>
    private const string Scope = "https://monitor.azure.com//.default";

    /// <summary>
    /// The expected cookie name created by Frontend
    /// </summary>
    private const string StampCookieName = "spstampcookie";

    public FrontendClientOptions(Uri baseEndpoint, string userAgent, TokenCredential tokenCredential, bool skipCertificateValidation = false)
    {
        if (baseEndpoint is null)
        {
            throw new ArgumentNullException(nameof(baseEndpoint));
        }

        // The CookiePolicy needs to be PerRetry because the cookie may
        // be updated in response to a 412 (Precondition failed).
        _ = this.SetEndpointRedirectionCachePolicy(baseEndpoint)
                .SetCacheControlPolicy(CacheControlPolicyType.NoCache)
                .SetCookiePolicy(baseEndpoint, StampCookieName, HttpPipelinePosition.PerRetry)
                .SetChallengeBasedAuthenticationPolicy(tokenCredential, Scope)
                .SetUserAgentPolicy(userAgent);

        if (skipCertificateValidation)
        {
#pragma warning disable CA2000 // Dispose objects before losing scope. The handler will be owned by the transport.
            var messageHandler = new HttpClientHandler()
            {
                // The pipeline handles redirects itself
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
#pragma warning restore CA2000 // Dispose objects before losing scope
            Transport = new HttpClientTransport(messageHandler);
        }
    }
}

