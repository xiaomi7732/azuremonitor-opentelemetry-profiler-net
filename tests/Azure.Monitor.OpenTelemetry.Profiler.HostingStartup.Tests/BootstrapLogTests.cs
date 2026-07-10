// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.IO;
using Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartupTests;

public class BootstrapLogTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    internal void TryResolveLogFilePath_WhenUnsetOrBlank_ReturnsFalse(string? envValue)
    {
        Assert.False(BootstrapLog.TryResolveLogFilePath(envValue, "/home", 123, out string path));
        Assert.Equal(string.Empty, path);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("  true  ")]
    internal void TryResolveLogFilePath_WhenEnabled_UsesDefaultPathUnderHome(string envValue)
    {
        Assert.True(BootstrapLog.TryResolveLogFilePath(envValue, "/srv/home", 4242, out string path));
        Assert.Equal(Path.Combine("/srv/home", "LogFiles", "AzureMonitorProfiler", "startup_4242.log"), path);
    }

    [Fact]
    internal void TryResolveLogFilePath_WhenHomeMissing_FallsBackToTempDir()
    {
        Assert.True(BootstrapLog.TryResolveLogFilePath("1", null, 7, out string path));
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "LogFiles", "AzureMonitorProfiler"), path);
        Assert.EndsWith("startup_7.log", path);
    }

    [Fact]
    internal void TryResolveLogFilePath_WhenExplicitPath_UsesItVerbatim()
    {
        string explicitPath = Path.Combine(Path.GetTempPath(), "amp-custom-startup.log");
        Assert.True(BootstrapLog.TryResolveLogFilePath(explicitPath, "/home", 9, out string path));
        Assert.Equal(explicitPath, path);
    }

    [Fact]
    internal void Logging_NeverThrows()
    {
        // The whole point of BootstrapLog is early, fail-safe logging; none of these may throw regardless of
        // whether the file sink is enabled/resolvable.
        BootstrapLog.Info("info message");
        BootstrapLog.Error("error without exception");
        BootstrapLog.Error("error with exception", new InvalidOperationException("boom"));
        BootstrapLog.Write("StartupHook", "info", "hook message");
    }
}
