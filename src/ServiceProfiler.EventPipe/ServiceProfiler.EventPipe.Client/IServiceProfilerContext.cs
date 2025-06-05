// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Core
{
    internal interface IServiceProfilerContext
    {
        Guid AppInsightsAppId { get; }
        Guid AppInsightsInstrumentationKey { get; }
        bool HasAppInsightsInstrumentationKey { get; }

        string MachineName { get; }
        CancellationTokenSource ServiceProfilerCancellationTokenSource { get; }
        Uri StampFrontendEndpointUrl { get; }

        event EventHandler<AppIdFetchedEventArgs> AppIdFetched;

        Task<Guid> GetAppInsightsAppIdAsync();
        void OnAppIdFetched(Guid appId);
    }
}
