using System;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal sealed class AppIdFetchedEventArgs : EventArgs
{
    public Guid AppId { get; }
    public AppIdFetchedEventArgs(Guid appId)
    {
        AppId = appId;
    }
}