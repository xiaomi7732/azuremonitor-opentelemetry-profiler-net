// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Azure.Monitor.OpenTelemetry.Profiler.OOPHost;

/// <summary>
/// A simple service to wait until the available of the target process.
/// TODO: Introduce other waiting strategy. 
/// </summary>
internal class TargetProcessService : ITargetProcess
{
    private readonly ServiceProfilerOOPHostOptions _options;
    private readonly ILogger _logger;

    public TargetProcessService(
        IOptions<ServiceProfilerOOPHostOptions> options,
        ILogger<TargetProcessService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int ProcessId { get; private set; }

    /// <summary>
    /// Try to get process id by name.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns> <summary>
    /// 
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<int> WaitUntilAvailableAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        string targetProcessName = _options.TargetProcessName;

        if (string.IsNullOrEmpty(targetProcessName))
        {
            _logger.LogError("Target process name is required. Profiler won't start.");
            return 0;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            Process? targetProcess = Process.GetProcessesByName(_options.TargetProcessName).FirstOrDefault();
            try
            {
                if (targetProcess is null)
                {
                    _logger.LogDebug("No process available by name of {name}", targetProcessName);
                    await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
                    targetProcess = Process.GetProcessesByName(_options.TargetProcessName).FirstOrDefault();
                    continue;
                }

                ProcessId = targetProcess.Id;
                return ProcessId;
            }
            finally
            {
                targetProcess?.Dispose();
            }
        }

        return 0;
    }
}