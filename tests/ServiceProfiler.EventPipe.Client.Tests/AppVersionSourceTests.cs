// -----------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Reflection;
using Microsoft.ApplicationInsights.Profiler.Shared.Services;
using Microsoft.ApplicationInsights.Profiler.Shared.Services.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ServiceProfiler.EventPipe.Client.Tests;

public class AppVersionSourceTests
{
    [Fact]
    public void AggregatedAppVersionSource_UsesFirstNonEmptyDetector()
    {
        var source = new AggregatedAppVersionSource(
            new IAppVersionDetector[]
            {
                new FakeDetector(null),
                new FakeDetector(""),
                new FakeDetector("2.5.0"),
                new FakeDetector("9.9.9"),
            },
            NullLogger<AggregatedAppVersionSource>.Instance);

        Assert.Equal("2.5.0", source.AppVersion);
    }

    [Fact]
    public void AggregatedAppVersionSource_WhenNoDetectorProvidesValue_ReturnsEmpty()
    {
        var source = new AggregatedAppVersionSource(
            new IAppVersionDetector[] { new FakeDetector(null), new FakeDetector("   ") },
            NullLogger<AggregatedAppVersionSource>.Instance);

        // "   " is non-empty per IsNullOrEmpty, so it is returned as-is; verify the all-empty case:
        var emptySource = new AggregatedAppVersionSource(
            new IAppVersionDetector[] { new FakeDetector(null), new FakeDetector("") },
            NullLogger<AggregatedAppVersionSource>.Instance);

        Assert.Equal("   ", source.AppVersion);
        Assert.Equal(string.Empty, emptySource.AppVersion);
    }

    [Fact]
    public void AggregatedAppVersionSource_WhenNoDetectors_ReturnsEmpty()
    {
        var source = new AggregatedAppVersionSource(
            new List<IAppVersionDetector>(),
            NullLogger<AggregatedAppVersionSource>.Instance);

        Assert.Equal(string.Empty, source.AppVersion);
    }

    [Fact]
    public void AssemblyAppVersionDetector_WithNullAssembly_ReturnsNull()
    {
        var detector = new AssemblyAppVersionDetector((Assembly?)null);
        Assert.Null(detector.GetAppVersion());
    }

    [Fact]
    public void AssemblyAppVersionDetector_WithRealAssembly_ReturnsNonEmptyVersion()
    {
        var detector = new AssemblyAppVersionDetector(typeof(AggregatedAppVersionSource).Assembly);
        Assert.False(string.IsNullOrEmpty(detector.GetAppVersion()));
    }

    private sealed class FakeDetector : IAppVersionDetector
    {
        private readonly string? _version;
        public FakeDetector(string? version) => _version = version;
        public string? GetAppVersion() => _version;
    }
}
