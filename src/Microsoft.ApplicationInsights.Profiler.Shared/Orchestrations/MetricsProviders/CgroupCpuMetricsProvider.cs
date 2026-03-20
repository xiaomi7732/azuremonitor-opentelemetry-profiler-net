//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.ServiceProfiler.Orchestration.MetricsProviders;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Orchestrations.MetricsProviders;

/// <summary>
/// Reads CPU usage from Linux cgroup stats. Supports cgroup v2 (/sys/fs/cgroup/cpu.stat)
/// with fallback to cgroup v1 (/sys/fs/cgroup/cpuacct/cpuacct.usage).
/// This is more reliable than Process.TotalProcessorTime in sandboxed environments
/// like Azure App Service Linux containers.
/// </summary>
internal sealed class CgroupCpuMetricsProvider : IMetricsProvider, IDisposable
{
    // cgroup v2
    private const string CgroupV2CpuStatPath = "/sys/fs/cgroup/cpu.stat";
    // cgroup v1
    private const string CgroupV1CpuUsagePath = "/sys/fs/cgroup/cpuacct/cpuacct.usage";

    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    private readonly ILogger _logger;
    private readonly int _cpuCount;
    private readonly CgroupVersion _version;
    private readonly Thread? _samplingThread;
    private readonly CancellationTokenSource _cts = new();

    private volatile float _nextValue;
    private long _lastUsageMicroseconds;
    private long _lastTimestampMs;

    public CgroupCpuMetricsProvider(ILogger<CgroupCpuMetricsProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cpuCount = Environment.ProcessorCount;

        if (File.Exists(CgroupV2CpuStatPath))
        {
            _version = CgroupVersion.V2;
            _logger.LogDebug("Using cgroup v2 CPU metrics from {path}", CgroupV2CpuStatPath);
        }
        else if (File.Exists(CgroupV1CpuUsagePath))
        {
            _version = CgroupVersion.V1;
            _logger.LogDebug("Using cgroup v1 CPU metrics from {path}", CgroupV1CpuUsagePath);
        }
        else
        {
            _version = CgroupVersion.None;
            _logger.LogWarning("No cgroup CPU stats found. CPU monitoring will report 0.");
            return;
        }

        // Use a dedicated thread instead of the thread pool (e.g. Task.Run/Task.Delay) because
        // CPU-bound workloads can saturate the thread pool, which would prevent this sampler from
        // running and cause CPU usage readings to stall at a stale value.
        _samplingThread = new Thread(SamplingLoop)
        {
            IsBackground = true,
            Name = "CgroupCpuMetricsSampler"
        };
        _samplingThread.Start();
    }

    public float GetNextValue() => _nextValue;

    private void SamplingLoop()
    {
        try
        {
            // Take initial snapshot
            _lastUsageMicroseconds = ReadCpuUsageMicroseconds();
            _lastTimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            _cts.Token.WaitHandle.WaitOne(UpdateInterval);

            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    long currentUsage = ReadCpuUsageMicroseconds();
                    long currentMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    long usageDelta = currentUsage - _lastUsageMicroseconds;
                    long wallDeltaMs = currentMs - _lastTimestampMs;

                    if (wallDeltaMs > 0 && usageDelta >= 0)
                    {
                        // usageDelta is in microseconds, wallDeltaMs is in milliseconds
                        // CPU% = (usageDelta_us / 1000) / (wallDelta_ms * cpuCount) * 100
                        double cpuPercent = (usageDelta / 1000.0) / (wallDeltaMs * _cpuCount) * 100.0;
                        _nextValue = (float)Math.Min(cpuPercent, 100.0);
                        _logger.LogDebug("CPU sample (cgroup): {CpuPercent:F2}%", _nextValue);
                    }

                    _lastUsageMicroseconds = currentUsage;
                    _lastTimestampMs = currentMs;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Cgroup CPU sampling iteration failed. Will retry on next interval.");
                }

                _cts.Token.WaitHandle.WaitOne(UpdateInterval);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cgroup CPU sampling loop terminated unexpectedly. CPU monitoring will stop functioning.");
        }
    }

    private long ReadCpuUsageMicroseconds()
    {
        return _version switch
        {
            CgroupVersion.V2 => ReadCgroupV2UsageMicroseconds(),
            CgroupVersion.V1 => ReadCgroupV1UsageMicroseconds(),
            _ => 0
        };
    }

    /// <summary>
    /// Reads usage_usec from /sys/fs/cgroup/cpu.stat (cgroup v2).
    /// Format: "usage_usec 123456"
    /// </summary>
    private long ReadCgroupV2UsageMicroseconds()
    {
        foreach (string line in File.ReadLines(CgroupV2CpuStatPath))
        {
            if (line.StartsWith("usage_usec", StringComparison.Ordinal))
            {
                int spaceIndex = line.IndexOf(' ');
                if (spaceIndex > 0 && long.TryParse(line.AsSpan(spaceIndex + 1), out long value))
                {
                    return value;
                }
            }
        }

        _logger.LogWarning("Could not find usage_usec in {path}", CgroupV2CpuStatPath);
        return 0;
    }

    /// <summary>
    /// Reads total CPU usage in nanoseconds from /sys/fs/cgroup/cpuacct/cpuacct.usage (cgroup v1),
    /// and converts to microseconds.
    /// </summary>
    private long ReadCgroupV1UsageMicroseconds()
    {
        string content = File.ReadAllText(CgroupV1CpuUsagePath).Trim();
        if (long.TryParse(content, out long nanoseconds))
        {
            return nanoseconds / 1000;
        }

        _logger.LogWarning("Could not parse CPU usage from {path}", CgroupV1CpuUsagePath);
        return 0;
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (_samplingThread != null && _samplingThread.Join(TimeSpan.FromSeconds(5)))
        {
            _cts.Dispose();
        }
    }

    private enum CgroupVersion
    {
        None,
        V1,
        V2
    }
}
