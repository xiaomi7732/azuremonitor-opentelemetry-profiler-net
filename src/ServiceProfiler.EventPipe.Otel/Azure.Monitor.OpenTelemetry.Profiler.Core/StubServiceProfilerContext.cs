using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.ServiceProfiler.Contract;

namespace Azure.Monitor.OpenTelemetry.Profiler.Core;

internal class StubServiceProfilerContext : IServiceProfilerContext
{
    [Obsolete("Use GetAppInsightsAppIdAsync() instead.", error: true)]
    public Guid AppInsightsAppId => throw new NotImplementedException();

    public Guid AppInsightsInstrumentationKey => Guid.Parse("5d8258e7-abb2-4066-89a5-8c73071b74ff");

    public bool HasAppInsightsInstrumentationKey => true;

    public string MachineName => "StubMachine";

    public CancellationTokenSource ServiceProfilerCancellationTokenSource => new();

    public Uri StampFrontendEndpointUrl { get; } = new Uri(FrontendEndpoints.ProdGlobal, UriKind.Absolute);

    public event EventHandler<AppIdFetchedEventArgs>? AppIdFetched;

    public Task<Guid> GetAppInsightsAppIdAsync()
    {
        throw new NotImplementedException();
    }

    public Task<Guid> GetAppInsightsAppIdAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(Guid.Parse("e54708db-77b6-457e-b577-103e84229f2c"));
    }

    public void OnAppIdFetched(Guid appId)
    {
        AppIdFetched?.Invoke(this, new AppIdFetchedEventArgs(appId));
    }
}