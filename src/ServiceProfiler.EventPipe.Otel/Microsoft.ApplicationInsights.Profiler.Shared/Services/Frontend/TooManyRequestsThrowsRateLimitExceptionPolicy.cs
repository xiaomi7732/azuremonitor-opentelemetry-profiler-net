// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using Azure.Core;
using Azure.Core.Pipeline;
using Microsoft.ServiceProfiler.Contract.Http;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Frontend;

/// <summary>
/// An <see cref="HttpPipelinePolicy"/> that throws a <see cref="RateLimitedException"/>
/// when the response is 429 (Too Many Requests)
/// </summary>
internal sealed class TooManyRequestsThrowsRateLimitExceptionPolicy : HttpPipelineSynchronousPolicy
{
    public override void OnReceivedResponse(HttpMessage message)
    {
        if (message.HasResponse && message.Response.Status == (int)HttpStatusCodeExtension.TooManyRequests)
        {
            throw new RateLimitedException();
        }

        base.OnReceivedResponse(message);
    }
}

