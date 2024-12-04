//-----------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static System.Globalization.CultureInfo;
using static Microsoft.ApplicationInsights.Profiler.Shared.Services.NativeMethods;

namespace Microsoft.ApplicationInsights.Profiler.Shared.Services;

internal static class EnvironmentUtilities
{
    // TODO: Wuyi. Verify the following is the correct environment variable.

    /// <summary>
    /// When the snapshot collector is injected by the AppInsights site extension,
    /// the version of the site extension will be set in this environmental variable.
    /// </summary>
    public static string EnablingSiteExtensionVersion => Environment.GetEnvironmentVariable("APPINSIGHTS_SNAPSHOT_COLLECTOR_EXTENSION");

    /// <summary>
    /// When the user enabled Snapshot from the Web App's portal, this environment variable will be set.
    /// </summary>
    public static string SnapshotFeatureVersion => Environment.GetEnvironmentVariable("APPSETTING_APPINSIGHTS_SNAPSHOTFEATURE_VERSION");
    // Kudu will map 'ABC' app setting as 'APPSETTING_ABC' environment variable.

    public static string CheckAndSanitizeMachineName(string input, bool throwIfInvalid = true)
    {
        input = input.Trim().ToLowerInvariant();
        // TODO: add validating rules here.
        if (string.IsNullOrEmpty(input))
        {
            if (throwIfInvalid)
            {
                throw new ArgumentException("Machine name can not be null or empty.");
            }

            input = null;
        }

        return input;
    }

    /// <summary>
    /// Get the Machine Name to use, with an appropriate suffix to distinguish different
    /// slots in an Antares environment.
    /// </summary>
    public static string MachineName
    {
        get
        {
            if (s_machineName == null)
            {
                string machineName = Environment.MachineName;

                // In Antares, you may have more than one agent running on the same machine.
                // Each website gets its own sandbox and that includes the deployment slots.
                // So we append a suffix to the machine name to distinguish the sandbox instances.
                if (IsRunningInAntares())
                {
                    // Use the IIS site name to generate a suffix. A typical value looks like "~1serviceprofiler__b914"
                    string iisSiteName = Environment.GetEnvironmentVariable("WEBSITE_IIS_SITE_NAME");

                    if (IsRunningInKudu(iisSiteName))
                    {
                        iisSiteName = iisSiteName.Substring(2);
                    }

                    machineName += iisSiteName;
                }

                s_machineName = CheckAndSanitizeMachineName(machineName);
            }

            return s_machineName;
        }
    }

    /// <summary>
    /// Create a short, random session Id. The Id is a 9 byte (72bit) random
    /// number encoded with base64 giving a string of length 12.
    /// Compare that to Guid.NewGuid().ToString() which is length 36.
    /// </summary>
    /// <returns>A base64-encoded random session ID.</returns>
    /// <remarks>
    /// 9 bytes was chosen as being suitably random (very low probability of
    /// collision) and also to get the most efficiency out of Base64 encoding
    /// (every 3 bytes encodes to 4 chars).
    /// </remarks>
    public static string CreateSessionId() => Convert.ToBase64String(EncodingUtilities.GetRandomBytes(length: 9));

    public static string HashedMachineName
    {
        get
        {
            if (string.IsNullOrEmpty(s_hashedMachineName))
            {
                s_hashedMachineName = EncodingUtilities.Anonymize(MachineName);
            }

            return s_hashedMachineName;
        }
    }

    /// <summary>
    /// Get the Machine Name to use, without the suffix to distinguish different
    /// slots in an Antares environment.
    /// </summary>
    public static string MachineNameWithoutSiteName => Environment.MachineName;

    public static string HashedMachineNameWithoutSiteName
    {
        get
        {
            if (string.IsNullOrEmpty(s_hashedMachineNameWithoutSiteName))
            {
                s_hashedMachineNameWithoutSiteName = EncodingUtilities.AnonymizeBase64(MachineNameWithoutSiteName);
            }

            return s_hashedMachineNameWithoutSiteName;
        }
    }

    /// <summary>
    /// Determine if this application is running in an Azure App Service environment.
    /// </summary>
    /// <returns>True if an Azure App Service environment (Antares) was detected.</returns>
    /// <remarks>
    /// Checks for the presence of the WEBSITE_INSTANCE_ID environment variable.
    /// Kudu does this too.
    /// </remarks>
    public static bool IsRunningInAntares() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));

    /// <summary>
    /// Gets the short region name based on the REGION_NAME environment variable
    /// set in Azure App Service.
    /// </summary>
    /// <returns>The short region name (e.g. "westus2") or null if the
    /// REGION_NAME environment variable is not set.</returns>
    public static string GetAntaresShortRegionName()
    {
        string regionName = Environment.GetEnvironmentVariable("REGION_NAME");
        return string.IsNullOrEmpty(regionName) ? null : regionName.ToLowerInvariant().Replace(" ", "");
    }

    /// <summary>
    /// Gets the full version of Antares (Azure App Service) that this application is running in.
    /// </summary>
    /// <returns>The version as a string or null if it could not be determined.</returns>
    /// <example>73.0.8598.30 (rd_websites_stable.180425-1555)</example>
    public static string GetAntaresVersion()
    {
        if (!IsRunningInAntares())
        {
            return null;
        }

        try
        {
            // Note: Hard-coding a path to an assembly in the GAC would usually be frowned upon.
            // However, we need a technique that works for both .NET Core and .NET Framework and
            // this runs only in Antares which is a well-known and stable environment.
            var webHostingAssemblyLocation = Environment.ExpandEnvironmentVariables(@"%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\Microsoft.Web.Hosting\v4.0_7.1.0.0__31bf3856ad364e35\Microsoft.Web.Hosting.dll");
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(webHostingAssemblyLocation);
            return fileVersionInfo.ProductVersion;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Determine whether the application is running in Azure Function environment.
    /// </summary>
    /// <returns>True if Azure Function environment was detected.</returns>
    public static bool IsRunningInAzureFunction() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FUNCTIONS_EXTENSION_VERSION"));

    /// <summary>
    /// Gets the full version of Azure Function that this application is running in
    /// </summary>
    /// <returns>The version as a string or null if it could not be determined.</returns>
    public static string GetAzureFunctionVersion()
    {
        try
        {
            var baseDir = Path.GetDirectoryName(typeof(EnvironmentUtilities).Assembly.Location);
            var functionHostAssemblyLocation = Path.Combine(baseDir, "Microsoft.Azure.WebJobs.Script.WebHost.dll");
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(functionHostAssemblyLocation);
            return fileVersionInfo.ProductVersion;
        }
        catch
        {
            return null;
        }
    }

    // See https://docs.microsoft.com/en-us/azure/service-fabric/service-fabric-environment-variables-reference
    // for a list of Service Fabric environment variables.
    public static bool IsRunningInServiceFabric()
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("Fabric_ApplicationName"));

    public static bool IsRunningInCloudService()
        => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RoleInstanceID"));

    /// <summary>
    /// Determine if this application is running in an Azure VM.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the Azure Instance Metadata Services API is reachable and returns compute information.</returns>
    /// <remarks>
    /// This API can take a while (~30 seconds) to time out if NOT running on an Azure VM, so it's a good idea
    /// to pass in a CancellationToken that will cancel after a shorter interval - say 5 seconds.
    /// </remarks>
    public static async Task<bool> IsRunningInAzureVmAsync(CancellationToken cancellationToken = default)
    {
        // Determine if this is an Azure VM by using Azure Instance Metadata Services
        var metadataServicesUri = new UriBuilder
        {
            Scheme = "HTTP",
            Host = "169.254.169.254",
            Path = "metadata/instance/compute",
            Query = "api-version=2017-08-01"
        };

        using HttpRequestMessage request = new(HttpMethod.Get, metadataServicesUri.Uri);
        request.Headers.Add("Metadata", "true");

        using HttpClient httpClient = new();
        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (HttpRequestException)
        {
            // Swallow the exception. Metadata services are not available on this machine.
        }

        return false;
    }

    /// <summary>
    /// Determine if this application is running in a Windows container.
    /// </summary>
    /// <returns>True if we can detect the presence of a Windows container.</returns>
    public static bool IsRunningInWindowsContainer()
    {
#if NETFRAMEWORK
            var containerTypeObj = Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control", "ContainerType", null);
            return containerTypeObj is int;
#else
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            uint size = sizeof(uint);
            var err = RegGetValueW((IntPtr)HKEY_LOCAL_MACHINE, @"System\CurrentControlSet\Control", "ContainerType", RRF_RT_DWORD, out uint type, out uint value, ref size);
            return err == 0 && type == REG_DWORD;
        }
        catch
        {
            // Not supported on this platform
            return false;
        }
#endif
    }

    public static bool IsProfilingSupportedInAntares(out string siteSku)
    {
        if (IsRunningInHyperVContainerAppService())
        {
            siteSku = "Hyper-V";
            return false;
        }

        siteSku = Environment.GetEnvironmentVariable("WEBSITE_SKU");
        if (string.IsNullOrEmpty(siteSku))
        {
            return false;
        }

        switch (siteSku.ToUpperInvariant())
        {
            case "DYNAMIC":
            case "FREE":
            case "SHARED":
                return false;
            default:
                return true;
        }
    }

    /// <summary>
    /// Get the shutdown file path, stored in a well-known environment variable for either Antares or our Monitor case.
    ///
    /// Returned value may be null or empty. This is the standalone execution mode when the user opens the program
    /// with no caller monitor. In that case, the user will be able to send a CtrlC signal to the window.
    /// </summary>
    public static string ShutdownFilePath
        => Environment.GetEnvironmentVariable(
            IsRunningInAntares() ? "WEBJOBS_SHUTDOWN_FILE" : "APPLICATIONINSIGHTSPROFILER_SHUTDOWN_FILE",
            EnvironmentVariableTarget.Process);

    public static string ExecutingAssemblyFileVersion
        => GetAssemblyFileVersion(Assembly.GetExecutingAssembly()) ?? "Unknown";

    /// <summary>
    /// Get the executing assembly's informational version. If the informational version is not
    /// present, then this falls back to the assembly file version.
    /// </summary>
    /// <remarks>
    /// The informational version may include pre-release designation such as "-beta".
    /// This is typically used in telemetry.
    /// </remarks>
    public static string ExecutingAssemblyInformationalVersion
    {
        get
        {
            var assembly = Assembly.GetExecutingAssembly();
            return GetAssemblyInformationalVersion(assembly) ?? GetAssemblyFileVersion(assembly) ?? "Unknown";
        }
    }

    /// <summary>
    /// Builds a string representing file version of the assembly with added prefix
    /// in format prefix:major.minor-revision.
    /// This string may be used for the SDK version in an Application Insights
    /// telemetry client.
    /// </summary>
    /// <param name="versionPrefix">Prefix to add to version. Usually has a trailing colon.</param>
    /// <returns>String representation of the version with prefix added.</returns>
    /// <remarks>
    /// Taken from
    /// https://github.com/microsoft/ApplicationInsights-dotnet/blob/main/BASE/src/Microsoft.ApplicationInsights/Extensibility/Implementation/SdkVersionUtils.cs
    /// </remarks>
    public static string GetApplicationInsightsSdkVersion(string versionPrefix)
    {
        if (!Version.TryParse(ExecutingAssemblyFileVersion, out Version version))
        {
            return null;
        }

        string postfix = version.Revision.ToString(InvariantCulture);
        return versionPrefix + version.ToString(fieldCount: 3) + "-" + postfix;
    }

    public static bool WasInstalledByDiagnosticServicesExtension()
    {
        return Directory.Exists(s_profilerWebJobFolder);
    }

    public static bool IsDiagnosticServicesExtensionEnabled()
    {
        var diagnosticServicesExtensionVersion = Environment.GetEnvironmentVariable("APPSETTING_DiagnosticServices_EXTENSION_VERSION");
        // When testing a private site extension on a production Antares instance it's impossible to disable the preinstalled extension without also triggering the self-removal logic.
        // An override stops the self-removal logic.
        var diagnosticServicesExtensionVersionOverride = Environment.GetEnvironmentVariable("APPSETTING_Override_DiagnosticServices_EXTENSION_VERSION");
        if (!string.IsNullOrEmpty(diagnosticServicesExtensionVersionOverride))
        {
            diagnosticServicesExtensionVersion = diagnosticServicesExtensionVersionOverride;
        }

        return !(string.IsNullOrEmpty(diagnosticServicesExtensionVersion) || diagnosticServicesExtensionVersion.Equals("disabled", StringComparison.Ordinal));
    }

    public static void RemoveWebJob()
    {
        Directory.Delete(s_profilerWebJobFolder, true);
    }

    /// <summary>
    /// Detect Windows Containers in Azure App Service.
    /// </summary>
    /// <returns>True if the app is running in a Hyper-V container in App Service.</returns>
    public static bool IsRunningInHyperVContainerAppService()
        => string.Equals(Environment.GetEnvironmentVariable("WEBSITE_ISOLATION"), "hyperv", StringComparison.OrdinalIgnoreCase);

    private static bool IsRunningInKudu(string iisSiteName)
    {
        // The "~1" indicates that it's running in the SCM (Kudu) instance, which is typical when this code
        // is running as a WebJob.
        return !string.IsNullOrEmpty(iisSiteName) && iisSiteName.StartsWith("~1", StringComparison.Ordinal);
    }

    private static readonly string s_profilerWebJobFolder = Environment.ExpandEnvironmentVariables(@"%HOME%\site\jobs\continuous\ApplicationInsightsProfiler3");

    private static string GetAssemblyFileVersion(Assembly assembly) => assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;

    private static string GetAssemblyInformationalVersion(Assembly assembly) => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    #region Private
    private static string s_machineName;
    private static string s_hashedMachineName;
    private static string s_hashedMachineNameWithoutSiteName;
    #endregion
}

