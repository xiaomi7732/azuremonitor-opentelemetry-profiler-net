// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Core.Pipeline;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Frontend;

/// <summary>
/// An <see cref="HttpPipelinePolicy"/> that calls a delegate when the
/// response is 412 (Precondition failed).
/// </summary>
internal sealed class PreconditionFailedCallbackPolicy : HttpPipelinePolicy
{
    private readonly Func<ValueTask> _onPreconditionFailedAsync;

    public PreconditionFailedCallbackPolicy(Func<ValueTask> onPreconditionFailedAsync)
    {
        _onPreconditionFailedAsync = onPreconditionFailedAsync ?? throw new ArgumentNullException(nameof(onPreconditionFailedAsync));
    }

    public override void Process(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        throw new NotImplementedException();
    }

    public override async ValueTask ProcessAsync(HttpMessage message, ReadOnlyMemory<HttpPipelinePolicy> pipeline)
    {
        await ProcessNextAsync(message, pipeline).ConfigureAwait(false);

        if (message.HasResponse && message.Response.Status == 412)
        {
            await _onPreconditionFailedAsync().ConfigureAwait(false);
        }
    }
}

