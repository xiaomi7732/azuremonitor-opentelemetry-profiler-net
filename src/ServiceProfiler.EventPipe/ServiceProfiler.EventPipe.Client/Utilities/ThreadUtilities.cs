using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ApplicationInsights.Profiler.Core
{
    public sealed class ThreadUtilities : IThreadUtilities
    {
        private ThreadUtilities() { }
        public static Lazy<ThreadUtilities> Instance { get; } = new Lazy<ThreadUtilities>(() => new ThreadUtilities(), LazyThreadSafetyMode.PublicationOnly);

        public async Task CallWithTimeoutAsync(Action action, TimeSpan timeout = default)
        {
            Task timeoutTask = Task.Delay(timeout);
            Task wrapper = Task.Run(action);

            Task completed = await Task.WhenAny(timeoutTask, wrapper).ConfigureAwait(false);

            if (!wrapper.IsCompleted)
            {
                throw new TimeoutException("Call didn't finish in time.");
            }
        }
    }
}
