// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Reflection;
using Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartupTests;

public class DependencyFloorCheckerTests
{
    private static AssemblyName Asm(string name, string version) => new(name) { Version = Version.Parse(version) };

    [Fact]
    internal void FindViolations_FlagsOnlyLoadedAssembliesBelowFloor()
    {
        Dictionary<string, Version> floors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["OpenTelemetry"] = Version.Parse("1.8.1.0"),
            ["Azure.Core"] = Version.Parse("1.46.1.0"),
        };

        AssemblyName[] loaded =
        {
            Asm("OpenTelemetry", "1.7.0.0"),   // below -> violation
            Asm("Azure.Core", "1.46.1.0"),     // equal -> ok
            Asm("System.Text.Json", "8.0.0.0"),// not in floors -> ignored
        };

        IReadOnlyList<DependencyFloorViolation> violations =
            PayloadDependencyFloorChecker.FindViolations(floors, loaded);

        DependencyFloorViolation v = Assert.Single(violations);
        Assert.Equal("OpenTelemetry", v.Name);
        Assert.Equal(Version.Parse("1.7.0.0"), v.Loaded);
        Assert.Equal(Version.Parse("1.8.1.0"), v.Required);
    }

    [Fact]
    internal void FindViolations_WhenLoadedAtOrAboveFloor_ReturnsNone()
    {
        Dictionary<string, Version> floors = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Azure.Core"] = Version.Parse("1.46.1.0"),
        };

        AssemblyName[] loaded = { Asm("Azure.Core", "1.46.1.0"), Asm("Azure.Core", "1.50.0.0") };

        Assert.Empty(PayloadDependencyFloorChecker.FindViolations(floors, loaded));
    }

    [Fact]
    internal void ReadFloorsFromDepsJson_ParsesAssemblyVersionsPerFile()
    {
        const string depsJson = """
            {
              "targets": {
                ".NETCoreApp,Version=v8.0": {
                  "OpenTelemetry/1.8.1": { "runtime": { "lib/net8.0/OpenTelemetry.dll": { "assemblyVersion": "1.8.1.0", "fileVersion": "1.8.1.100" } } },
                  "Azure.Core/1.46.1": { "runtime": { "lib/net8.0/Azure.Core.dll": { "assemblyVersion": "1.46.1.0" } } },
                  "NoRuntime/1.0.0": { "dependencies": {} },
                  "NoAssemblyVersion/1.0.0": { "runtime": { "lib/net8.0/Thing.dll": { "fileVersion": "1.0.0.0" } } }
                }
              }
            }
            """;

        IReadOnlyDictionary<string, Version> floors = PayloadDependencyFloorChecker.ReadFloorsFromDepsJson(depsJson);

        Assert.Equal(Version.Parse("1.8.1.0"), floors["OpenTelemetry"]);
        Assert.Equal(Version.Parse("1.46.1.0"), floors["Azure.Core"]);
        Assert.False(floors.ContainsKey("Thing")); // no assemblyVersion -> not a floor
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    internal void ReadFloorsFromDepsJson_WhenEmptyOrNoTargets_ReturnsEmpty(string depsJson)
    {
        if (depsJson == "not json")
        {
            Assert.ThrowsAny<Exception>(() => PayloadDependencyFloorChecker.ReadFloorsFromDepsJson(depsJson));
            return;
        }

        Assert.Empty(PayloadDependencyFloorChecker.ReadFloorsFromDepsJson(depsJson));
    }
}
