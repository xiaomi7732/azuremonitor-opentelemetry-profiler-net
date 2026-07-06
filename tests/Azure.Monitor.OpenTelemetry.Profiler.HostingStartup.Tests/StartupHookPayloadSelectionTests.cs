// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.IO;

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.Tests;

/// <summary>
/// Tests for <see cref="StartupHook.SelectPayloadDirectory(string, int)"/>, which picks the per-target-
/// framework payload subfolder (net8.0/, net9.0/, net10.0/, ...) matching the running runtime major.
/// </summary>
public sealed class StartupHookPayloadSelectionTests : IDisposable
{
    private readonly string _root;

    public StartupHookPayloadSelectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sp-payload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private void CreateTfmFolders(params string[] names)
    {
        foreach (string name in names)
        {
            Directory.CreateDirectory(Path.Combine(_root, name));
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void SelectsExactMatchWhenPresent(int runtimeMajor)
    {
        CreateTfmFolders("net8.0", "net9.0", "net10.0");

        string? selected = StartupHook.SelectPayloadDirectory(_root, runtimeMajor);

        Assert.Equal(Path.Combine(_root, $"net{runtimeMajor}.0"), selected);
    }

    [Fact]
    public void FallsBackToHighestLowerOrEqualWhenExactMissing()
    {
        // A future .NET 11 app with only net8/9/10 payloads should use net10.0.
        CreateTfmFolders("net8.0", "net9.0", "net10.0");

        string? selected = StartupHook.SelectPayloadDirectory(_root, runtimeMajor: 11);

        Assert.Equal(Path.Combine(_root, "net10.0"), selected);
    }

    [Fact]
    public void FallsBackToHighestAvailableBelowRuntime()
    {
        // Only net8.0 present; a .NET 10 app should still pick net8.0.
        CreateTfmFolders("net8.0");

        string? selected = StartupHook.SelectPayloadDirectory(_root, runtimeMajor: 10);

        Assert.Equal(Path.Combine(_root, "net8.0"), selected);
    }

    [Fact]
    public void ReturnsNullWhenAllFoldersAreNewerThanRuntime()
    {
        // Never select a payload newer than the runtime (the runtime can't roll a framework assembly down).
        CreateTfmFolders("net9.0", "net10.0");

        string? selected = StartupHook.SelectPayloadDirectory(_root, runtimeMajor: 8);

        Assert.Null(selected);
    }

    [Fact]
    public void ReturnsNullWhenNoTfmFoldersExist()
    {
        // Root exists but contains no net*.0 folders (e.g. only StartupHook.dll and Uploader\).
        Directory.CreateDirectory(Path.Combine(_root, "Uploader"));

        string? selected = StartupHook.SelectPayloadDirectory(_root, runtimeMajor: 10);

        Assert.Null(selected);
    }

    [Fact]
    public void IgnoresNonFrameworkFolders()
    {
        CreateTfmFolders("net8.0");
        Directory.CreateDirectory(Path.Combine(_root, "Uploader"));
        Directory.CreateDirectory(Path.Combine(_root, "notatfm"));

        string? selected = StartupHook.SelectPayloadDirectory(_root, runtimeMajor: 9);

        Assert.Equal(Path.Combine(_root, "net8.0"), selected);
    }
}
