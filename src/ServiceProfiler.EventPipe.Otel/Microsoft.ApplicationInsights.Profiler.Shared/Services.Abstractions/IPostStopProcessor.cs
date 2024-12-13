using System.Threading;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Profiler.Shared.Contracts;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;

internal interface IPostStopProcessor
{
    Task PostStopProcessAsync(PostStopOptions state, CancellationToken cancellationToken);
}