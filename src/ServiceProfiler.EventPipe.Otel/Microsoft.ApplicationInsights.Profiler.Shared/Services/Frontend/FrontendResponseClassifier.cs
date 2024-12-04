// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System.Net;
using Azure.Core;
using Microsoft.ServiceProfiler.Contract.Http;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Frontend;

/// <summary>
/// A <see cref="ResponseClassifier"/> for the <see cref="StampFrontendClient"/>
/// that prevents retries on 429 (Too Many Requests) responses and
/// retries on 412 (Precondition failed)
/// </summary>
internal sealed class FrontendResponseClassifier : ResponseClassifier
{
    public override bool IsRetriableResponse(HttpMessage message)
    {
        switch ((HttpStatusCode)message.Response.Status)
        {
            case HttpStatusCode.PreconditionFailed: // Precondition failed (invalid cookie)
                return true;

            case HttpStatusCodeExtension.TooManyRequests: // Too Many Requests (rate limit)
                return false;

            default:
                break;
        }

        return base.IsRetriableResponse(message);
    }
}

