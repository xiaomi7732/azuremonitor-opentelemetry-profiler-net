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

# The profiler projects are netstandard2.1 (runtime-version-flexible); only the bundled framework/extension
# dependency DLLs differ per runtime. We publish one HostingStartup closure per target framework, each with
# the framework/extension package versions overridden to that major's baseline, so the bundled versions
# match what the app's shared framework already provides (the runtime never rolls a shared-framework
# assembly DOWN, so a 10.x bundle cannot load on a .NET 8/9 app). At runtime the StartupHook resolver picks
# the payload\<version>\net{major}.0\ folder matching Environment.Version.Major.
#
# NOTE (net8.0): the repo's Azure.Monitor.OpenTelemetry.Exporter 1.4.0 floors OpenTelemetry at 1.12.0, and
# OpenTelemetry 1.12.0 requires Microsoft.Extensions.* >= 9.0.0 - so net8.0 would additionally require
# dropping the exporter below 1.4.0 (changes the profiler's exporter integration). net8.0 is therefore not
# published yet; net9.0 + net10.0 are. Adding "net8.0" needs an exporter down-level (see plan.md "A2").
$targetFrameworks = @("net9.0", "net10.0")

# Per-TFM overrides for the version PROPERTIES defined in the Directory.Packages.props files. net10.0 uses
# the repo defaults (OpenTelemetry 1.15.3 / extensions 10.0.0). net9.0 down-levels OpenTelemetry to 1.12.0
# (the floor allowed by exporter 1.4.0) and the shared-framework extension packages to 9.0.x so the bundled
# versions match a .NET 9 app's shared framework. The profiler builds from source, so it recompiles against
# OpenTelemetry 1.12 for the net9.0 payload (no compiled-against-1.15 mismatch).
$tfmVersionOverrides = @{
    "net9.0" = @{
        "_OpenTelemetryVersion"                            = "1.12.0"
        "_MicrosoftExtensionsVersion"                      = "9.0.0"
        "_MicrosoftExtensionsOptionsVersion"               = "9.0.0"
        "_SystemTextJsonVersion"                           = "9.0.0"
        "_SystemDiagnosticsDiagnosticSourceVersion"        = "9.0.0"
        "_SystemMemoryDataVersion"                         = "9.0.0"
        "_SystemSecurityCryptographyProtectedDataVersion"  = "9.0.0"
        "_SystemTextEncodingsWebVersion"                   = "9.0.0"
        "_SystemIOHashingVersion"                          = "9.0.0"
    }
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

# 2. Publish the HostingStartup closure once per target framework into payload\<version>\<tfm>\, overriding
#    the framework/extension package versions to that major's baseline.
#
#    IMPORTANT: the profiler projects are shared netstandard2.1 libraries that get recompiled against each
#    TFM's overridden dependency versions (e.g. OpenTelemetry 1.12.0 + Microsoft.Extensions.* 9.0 for net9.0
#    vs 1.15.3 + 10.0 for net10.0). Because MSBuild/NuGet cache per-project bin/obj, a shared project built
#    for one TFM would otherwise be reused for the next, bleeding the wrong versions into the payload. So we
#    clean bin/obj under src (and shut down the build server) before each TFM publish to force a clean,
#    consistent rebuild. This is why per-TFM builds are slower.
$multiTfm = $targetFrameworks.Count -gt 1
foreach ($tfm in $targetFrameworks) {
    if ($multiTfm) {
        Write-Host "==> Cleaning bin/obj under src for a clean $tfm build" -ForegroundColor Cyan
        dotnet build-server shutdown 2>&1 | Out-Null
        Get-ChildItem -Recurse -Directory -Path $srcDir -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' } |
            ForEach-Object { Remove-Item -Recurse -Force $_.FullName -ErrorAction SilentlyContinue }
    }
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
