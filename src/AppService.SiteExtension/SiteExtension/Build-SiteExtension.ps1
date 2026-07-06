<#
.SYNOPSIS
    Builds the codeless Azure Monitor OpenTelemetry Profiler Site Extension (.nupkg) for Windows App Service.

.DESCRIPTION
    Produces a staging layout:
        staging/
          applicationHost.xdt
          payload/
            <version>/
              Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll   (assembly-resolution bootstrap)
              Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.dll (detect + route profiler)
              <all profiler + dependency assemblies>
              Uploader/
                Microsoft.ApplicationInsights.Profiler.Uploader.dll   (out-of-proc trace uploader)
    then packs it into Out/NuGets/Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.<version>.nupkg
    with the AzureSiteExtension tag so it appears in the Kudu Site Extensions gallery.

    The payload is staged under a VERSION-STAMPED subfolder (payload\<version>\) so that upgrades never
    have to overwrite a DLL that a running SCM/app worker still holds locked (the DOTNET_STARTUP_HOOKS /
    ASPNETCORE_HOSTINGSTARTUPASSEMBLIES hooks are loaded by every .NET-Core worker on the site, including
    the persistent Kudu/SCM worker). A new version stages into a new folder; the applicationHost.xdt is
    generated with the version baked into the payload paths (the {{PAYLOAD_SUBDIR}} token). This mirrors how
    the AI agent versions its own StartupHook path.

    The produced package targets net8.0 applications (the POC runtime). Apps on other runtimes need a
    payload published for the matching target framework.

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER Version
    Package version. Default: 0.1.0-poc.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Version = "0.1.0-poc"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$here      = $PSScriptRoot
$extRoot   = Split-Path -Parent $here
$srcDir    = Split-Path -Parent $extRoot
$repoRoot  = Split-Path -Parent $srcDir

$hostingStartupProj = Join-Path $extRoot "Azure.Monitor.OpenTelemetry.Profiler.HostingStartup\Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.csproj"
$startupHookProj    = Join-Path $extRoot "Azure.Monitor.OpenTelemetry.Profiler.StartupHook\Azure.Monitor.OpenTelemetry.Profiler.StartupHook.csproj"
$xdtTransformsProj  = Join-Path $extRoot "Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms\Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms.csproj"
$uploaderProj       = Join-Path $srcDir  "ServiceProfiler.EventPipe.Upload\ServiceProfiler.EventPipe.Upload.csproj"
$nuspec             = Join-Path $here    "Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.nuspec"

$staging     = Join-Path $here "staging"
$payloadRoot = Join-Path $staging "payload\$Version"
$uploaderOut = Join-Path $payloadRoot "Uploader"
$outDir      = Join-Path $repoRoot "Out\NuGets"

# Uploader is a standalone out-of-proc process, independent of the profiled app's runtime.
$uploaderFramework = "net8.0"

# The profiler projects are netstandard2.1 (runtime-version-flexible). The HostingStartup project is
# multi-targeted (net8.0;net9.0;net10.0); we publish its closure once per target framework so the runtime
# gets a matching-TFM HostingStartup assembly. The bundled dependency DLLs (Microsoft.Extensions.*, OTel,
# System.Text.Json, ...) use their 10.x versions, which ship net8.0/net9.0/net10.0 assets and therefore run
# on all three runtimes - so NO per-runtime down-leveling is needed. At runtime the StartupHook resolver
# picks the payload\<version>\net{major}.0\ folder matching Environment.Version.Major.
$targetFrameworks = @("net8.0", "net9.0", "net10.0")

# Per-TFM version-property overrides. Left empty on purpose: all TFMs build with the repo-default (10.x)
# dependency versions, whose per-TFM NuGet assets are compatible with net8/net9/net10. If a future live test
# on a real .NET 8/9 app shows a runtime type-identity conflict at the app<->profiler boundary (most likely
# System.Diagnostics.DiagnosticSource, which drives request<->sample correlation), align ONLY that assembly
# for the affected TFM here (e.g. "_SystemDiagnosticsDiagnosticSourceVersion" = "8.0.1") rather than
# wholesale down-leveling. See plan.md "A2b".
$tfmVersionOverrides = @{
    "net8.0"  = @{}
    "net9.0"  = @{}
    "net10.0" = @{}
}

function Invoke-Checked([string]$description, [scriptblock]$action) {
    Write-Host "==> $description" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) {
        throw "$description failed with exit code $LASTEXITCODE."
    }
}

# 1. Clean staging.
Write-Host "==> Cleaning staging folder" -ForegroundColor Cyan
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force -Path $payloadRoot | Out-Null
New-Item -ItemType Directory -Force -Path $outDir      | Out-Null

# 2. Publish the HostingStartup closure once per target framework into payload\<version>\<tfm>\. All TFMs
#    build with the repo-default dependency versions, so there is no cross-TFM version bleed to guard
#    against; the multi-targeted HostingStartup produces a matching-TFM assembly per folder while the shared
#    netstandard2.1 profiler projects compile once and are reused.
foreach ($tfm in $targetFrameworks) {
    $tfmDir = Join-Path $payloadRoot $tfm
    $overrideArgs = @()
    foreach ($kvp in $tfmVersionOverrides[$tfm].GetEnumerator()) {
        $overrideArgs += "-p:$($kvp.Key)=$($kvp.Value)"
    }
    Invoke-Checked "Publishing HostingStartup payload ($tfm)" {
        dotnet publish $hostingStartupProj -c $Configuration -f $tfm -o $tfmDir --nologo @overrideArgs
    }
}

# 3. Build the resolver StartupHook once (BCL-only, TFM-agnostic) and copy its single assembly to the
#    payload root. It runs on every runtime and selects the matching net{major}.0 subfolder at load time.
Invoke-Checked "Building StartupHook resolver" {
    dotnet build $startupHookProj -c $Configuration -f $uploaderFramework --nologo
}
$startupHookDll = Join-Path $extRoot "Azure.Monitor.OpenTelemetry.Profiler.StartupHook\bin\$Configuration\$uploaderFramework\Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll"
if (-not (Test-Path $startupHookDll)) { throw "StartupHook assembly not found at $startupHookDll." }
Copy-Item $startupHookDll -Destination $payloadRoot -Force

# 4. Publish the out-of-proc uploader once into payload\<version>\Uploader.
Invoke-Checked "Publishing the trace uploader ($uploaderFramework)" {
    dotnet publish $uploaderProj -c $Configuration -f $uploaderFramework -o $uploaderOut --nologo
}

# 5. Build the applicationHost.xdt custom transform (net472) and copy just its assembly into the
#    staging root, next to applicationHost.xdt. Microsoft.Web.XmlTransform.dll is intentionally NOT
#    shipped - the App Service XDT engine provides it at runtime.
Invoke-Checked "Building the applicationHost.xdt custom transform" {
    dotnet build $xdtTransformsProj -c $Configuration --nologo
}
$xdtTransformsDll = Join-Path $extRoot "Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms\bin\$Configuration\Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms.dll"
if (-not (Test-Path $xdtTransformsDll)) { throw "XDT transform assembly not found at $xdtTransformsDll." }
Copy-Item $xdtTransformsDll -Destination $staging -Force

# 6. Generate the applicationHost.xdt into the staging root, baking the version-stamped payload subfolder
#    into the {{PAYLOAD_SUBDIR}} token so the DOTNET_STARTUP_HOOKS / SP_UPLOADER_PATH paths point at
#    payload\<version>\.
$xdtTemplate = Get-Content -Raw -Path (Join-Path $here "applicationHost.xdt")
if ($xdtTemplate -notmatch '\{\{PAYLOAD_SUBDIR\}\}') {
    throw "applicationHost.xdt is missing the {{PAYLOAD_SUBDIR}} token; cannot version-stamp the payload paths."
}
$xdtGenerated = $xdtTemplate -replace '\{\{PAYLOAD_SUBDIR\}\}', $Version
Set-Content -Path (Join-Path $staging "applicationHost.xdt") -Value $xdtGenerated -Encoding UTF8

# 7. Pack the site extension.
$nuget = (Get-Command nuget -ErrorAction SilentlyContinue)?.Source
if (-not $nuget) { $nuget = "C:\Program Files\NuGet\nuget.exe" }
if (-not (Test-Path $nuget)) { throw "nuget.exe not found. Install NuGet CLI or add it to PATH." }

Invoke-Checked "Packing the site extension" {
    & $nuget pack $nuspec -BasePath $here -OutputDirectory $outDir -Version $Version -NoDefaultExcludes -NonInteractive
}

$package = Join-Path $outDir "Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.$Version.nupkg"
Write-Host ""
Write-Host "Site extension package created:" -ForegroundColor Green
Write-Host "  $package"
