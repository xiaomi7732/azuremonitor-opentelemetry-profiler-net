using System;
using System.Threading;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal sealed class BootstrapState : IDisposable
{
    private bool _isDisposed = false;

    private readonly ManualResetEventSlim _eventWaitHandle = new(initialState: false);
    private volatile bool _isProfilerRunning;

    /// <summary>
    /// Sets the profiler running state and signals any waiters.
    /// </summary>
    public void SetProfilerRunning(bool isRunning)
    {
        _isProfilerRunning = isRunning;
        _eventWaitHandle.Set();
    }

    /// <summary>
    /// Waits until bootstrap is completed and returns whether the profiler is running.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns>>True if profiler is running; false otherwise.</returns>
    public bool IsProfilerRunning(CancellationToken cancellationToken)
    {
        _eventWaitHandle.Wait(cancellationToken);
        return _isProfilerRunning;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            _eventWaitHandle.Dispose();
        }

        _isDisposed = true;
    }
}