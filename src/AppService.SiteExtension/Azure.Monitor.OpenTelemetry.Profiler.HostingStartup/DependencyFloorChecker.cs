// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace Azure.Monitor.OpenTelemetry.Profiler.HostingStartup;

/// <summary>A shared dependency the application loaded at a version below what the profiler payload requires.</summary>
internal readonly struct DependencyFloorViolation
{
    public DependencyFloorViolation(string name, Version loaded, Version required)
    {
        Name = name;
        Loaded = loaded;
        Required = required;
    }

    public string Name { get; }
    public Version Loaded { get; }
    public Version Required { get; }
}

/// <summary>
/// Pre-flight check that compares the versions of shared assemblies the application has already loaded
/// (in the default load context, where the profiler binds) against the versions the chosen profiler payload
/// was built against (its <c>*.deps.json</c> floors). It lets the router back off with a specific, actionable log when a below-floor dependency would make activation fail,
/// instead of relying solely on the fail-safe try/catch to swallow a JIT-time
/// <see cref="System.IO.FileNotFoundException"/>. It is best-effort and additive: it only sees assemblies
/// already loaded at check time, so the guarded activation remains the backstop for anything that loads later.
/// </summary>
internal interface IDependencyFloorChecker
{
    IReadOnlyList<DependencyFloorViolation> CheckLoadedAgainstPayloadFloors(TelemetryStack stack);
}

/// <summary>
/// Default <see cref="IDependencyFloorChecker"/>: reads the chosen activator's <c>*.deps.json</c> (staged in
/// the stack's payload subfolder) for the per-assembly floors, then compares them against the assemblies
/// currently loaded in the process. Fully fail-safe - any error yields "no violations" so a checker problem
/// never blocks activation (the guarded activation still protects the host).
/// </summary>
internal sealed class PayloadDependencyFloorChecker : IDependencyFloorChecker
{
    public IReadOnlyList<DependencyFloorViolation> CheckLoadedAgainstPayloadFloors(TelemetryStack stack)
    {
        try
        {
            if (!ReflectionProfilerActivatorInvoker.TryGetActivator(stack, out string assemblyName, out _))
            {
                return Array.Empty<DependencyFloorViolation>();
            }

            string? routerDir = System.IO.Path.GetDirectoryName(typeof(PayloadDependencyFloorChecker).Assembly.Location);
            string subfolder = DetectedStackAppContextData.ToPayloadSubfolder(stack);
            if (string.IsNullOrEmpty(routerDir) || string.IsNullOrEmpty(subfolder))
            {
                return Array.Empty<DependencyFloorViolation>();
            }

            string depsPath = System.IO.Path.Combine(routerDir!, subfolder, assemblyName + ".deps.json");
            if (!System.IO.File.Exists(depsPath))
            {
                return Array.Empty<DependencyFloorViolation>();
            }

            IReadOnlyDictionary<string, Version> floors = ReadFloorsFromDepsJson(System.IO.File.ReadAllText(depsPath));
            // Only the DEFAULT load context matters: the profiler payload is loaded there (the StartupHook
            // resolver hooks AssemblyLoadContext.Default, and the activator is loaded via Assembly.Load). A
            // below-floor dependency isolated in a custom AssemblyLoadContext (e.g. a plugin) cannot conflict
            // with the profiler's binding, so scoping to the default context avoids a false back-off.
            IEnumerable<AssemblyName> loaded = AssemblyLoadContext.Default.Assemblies.Select(a => a.GetName());
            return FindViolations(floors, loaded);
        }
        catch
        {
            // Never let the pre-flight itself break activation; fall through to the guarded activation.
            return Array.Empty<DependencyFloorViolation>();
        }
    }

    /// <summary>
    /// Parses a <c>*.deps.json</c> document into a map of assembly simple name -> assembly version, using the
    /// <c>assemblyVersion</c> recorded for each shipped runtime file. These are the exact versions the payload
    /// references (its binding floors), so the map is self-maintaining. Testable and side-effect free.
    /// </summary>
    internal static IReadOnlyDictionary<string, Version> ReadFloorsFromDepsJson(string depsJson)
    {
        Dictionary<string, Version> floors = new(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(depsJson))
        {
            return floors;
        }

        using JsonDocument doc = JsonDocument.Parse(depsJson);
        if (!doc.RootElement.TryGetProperty("targets", out JsonElement targets) || targets.ValueKind != JsonValueKind.Object)
        {
            return floors;
        }

        foreach (JsonProperty target in targets.EnumerateObject())
        {
            if (target.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (JsonProperty library in target.Value.EnumerateObject())
            {
                if (library.Value.ValueKind != JsonValueKind.Object ||
                    !library.Value.TryGetProperty("runtime", out JsonElement runtime) ||
                    runtime.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (JsonProperty file in runtime.EnumerateObject())
                {
                    if (file.Value.ValueKind != JsonValueKind.Object ||
                        !file.Value.TryGetProperty("assemblyVersion", out JsonElement av) ||
                        av.ValueKind != JsonValueKind.String ||
                        !Version.TryParse(av.GetString(), out Version? version) ||
                        version is null)
                    {
                        continue;
                    }

                    string name = System.IO.Path.GetFileNameWithoutExtension(file.Name);
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    // Keep the highest version seen for a given simple name.
                    if (!floors.TryGetValue(name, out Version? existing) || version > existing)
                    {
                        floors[name] = version;
                    }
                }
            }
        }

        return floors;
    }

    /// <summary>
    /// Returns the loaded assemblies whose version is strictly below the payload floor for the same simple
    /// name. Assemblies not in <paramref name="floors"/> (e.g. the app's own or the profiler's private
    /// assemblies) are ignored, which naturally scopes the check to shared dependencies. Testable and
    /// side-effect free.
    /// </summary>
    internal static IReadOnlyList<DependencyFloorViolation> FindViolations(
        IReadOnlyDictionary<string, Version> floors,
        IEnumerable<AssemblyName> loadedAssemblies)
    {
        List<DependencyFloorViolation> violations = new();
        foreach (AssemblyName loaded in loadedAssemblies)
        {
            if (loaded.Name is string name
                && loaded.Version is Version loadedVersion
                && floors.TryGetValue(name, out Version? required)
                && loadedVersion < required)
            {
                violations.Add(new DependencyFloorViolation(name, loadedVersion, required));
            }
        }

        return violations;
    }
}

/// <summary>An <see cref="IDependencyFloorChecker"/> that never reports violations (used as a test default).</summary>
internal sealed class NoDependencyFloorChecker : IDependencyFloorChecker
{
    public static readonly NoDependencyFloorChecker Instance = new();

    public IReadOnlyList<DependencyFloorViolation> CheckLoadedAgainstPayloadFloors(TelemetryStack stack) =>
        Array.Empty<DependencyFloorViolation>();
}
