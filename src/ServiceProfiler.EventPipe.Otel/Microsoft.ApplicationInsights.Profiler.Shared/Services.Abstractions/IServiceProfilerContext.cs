// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

    internal interface IServiceProfilerContext
    {
        [Obsolete("Use GetAppInsightsAppIdAsync() instead.", error: true)]
        Guid AppInsightsAppId { get; }
        
        Guid AppInsightsInstrumentationKey { get; }
        bool HasAppInsightsInstrumentationKey { get; }

        string MachineName { get; }
        CancellationTokenSource ServiceProfilerCancellationTokenSource { get; }
        Uri StampFrontendEndpointUrl { get; }

        event EventHandler<AppIdFetchedEventArgs> AppIdFetched;

        Task<Guid> GetAppInsightsAppIdAsync();
        Task<Guid> GetAppInsightsAppIdAsync(CancellationToken cancellationToken);
        
        void OnAppIdFetched(Guid appId);
    }
