// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartupTests;

public class DepsFileTelemetryStackDetectorTests
{
    private const string OpenTelemetryDeps = """
        { "libraries": { "SampleApp/1.0.0": {}, "OpenTelemetry/1.9.0": {}, "OpenTelemetry.Extensions.Hosting/1.9.0": {} } }
        """;

    private const string AzureMonitorDistroDeps = """
        { "libraries": { "SampleApp/1.0.0": {}, "Azure.Monitor.OpenTelemetry.AspNetCore/1.2.0": {}, "OpenTelemetry/1.9.0": {} } }
        """;

    private const string ApplicationInsightsDeps = """
        { "libraries": { "SampleApp/1.0.0": {}, "Microsoft.ApplicationInsights.AspNetCore/2.22.0": {}, "Microsoft.ApplicationInsights/2.22.0": {} } }
        """;

    // The classic AI ASP.NET Core SDK (2.22+) transitively pulls in the OpenTelemetry SDK; this must
    // still be classified as ApplicationInsights, not Both.
    private const string ClassicWithTransitiveOTelDeps = """
        { "libraries": { "SampleApp/1.0.0": {}, "Microsoft.ApplicationInsights.AspNetCore/2.23.0": {}, "Azure.Monitor.OpenTelemetry.Exporter/1.8.0": {}, "OpenTelemetry/1.15.3": {}, "OpenTelemetry.Extensions.Hosting/1.15.3": {} } }
        """;

    private const string WorkerServiceDeps = """
        { "libraries": { "SampleApp/1.0.0": {}, "Microsoft.ApplicationInsights.WorkerService/2.23.0": {}, "OpenTelemetry/1.15.3": {} } }
        """;

    private const string NeitherDeps = """
        { "libraries": { "SampleApp/1.0.0": {}, "Newtonsoft.Json/13.0.3": {} } }
        """;

    [Theory]
    [InlineData(OpenTelemetryDeps, TelemetryStack.OpenTelemetry)]
    [InlineData(AzureMonitorDistroDeps, TelemetryStack.OpenTelemetry)]
    [InlineData(ApplicationInsightsDeps, TelemetryStack.ApplicationInsights)]
    [InlineData(ClassicWithTransitiveOTelDeps, TelemetryStack.ApplicationInsights)]
    [InlineData(WorkerServiceDeps, TelemetryStack.ApplicationInsights)]
    [InlineData(NeitherDeps, TelemetryStack.None)]
    internal void DetectFromDepsJson_ClassifiesStack(string depsJson, TelemetryStack expected)
    {
        Assert.Equal(expected, DepsFileTelemetryStackDetector.DetectFromDepsJson(depsJson));
    }

    [Fact]
    internal void DetectFromDepsJson_WhenNoLibrariesSection_ReturnsNone()
    {
        Assert.Equal(TelemetryStack.None, DepsFileTelemetryStackDetector.DetectFromDepsJson("{}"));
    }

    [Fact]
    internal void DetectFromDepsJson_WhenMalformed_ReturnsNone()
    {
        Assert.Equal(TelemetryStack.None, DepsFileTelemetryStackDetector.DetectFromDepsJson("not json"));
    }

    [Fact]
    internal void Detect_WhenDepsPathNull_ReturnsNone()
    {
        DepsFileTelemetryStackDetector detector = new(depsFilePathProvider: () => null, readAllText: _ => null);
        Assert.Equal(TelemetryStack.None, detector.Detect());
    }

    [Fact]
    internal void Detect_ReadsAndClassifiesProvidedDeps()
    {
        DepsFileTelemetryStackDetector detector = new(
            depsFilePathProvider: () => @"C:\app\SampleApp.deps.json",
            readAllText: _ => OpenTelemetryDeps);

        Assert.Equal(TelemetryStack.OpenTelemetry, detector.Detect());
    }
}
