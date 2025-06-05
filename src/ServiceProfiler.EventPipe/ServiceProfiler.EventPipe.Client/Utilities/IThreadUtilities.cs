using System;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Core
{
    public interface IThreadUtilities
    {
        Task CallWithTimeoutAsync(Action action, TimeSpan timeout = default);
    }
}
