using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface IPostStopProcessor
{
        Task PostStopProcessAsync(PostStopOptions e, CancellationToken cancellationToken)

}
