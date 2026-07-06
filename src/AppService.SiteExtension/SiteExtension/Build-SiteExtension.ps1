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
# NOTE (blocker for net8.0/net9.0): the repo pins OpenTelemetry to 1.15.3, whose graph transitively
# requires Microsoft.Extensions.* >= 10.0.0 (via Microsoft.Extensions.Logging.Configuration 10.0.0), and
# Azure.Core/System.ClientModel add further 8.0.x floors. So a net8/net9 payload cannot simply down-level
# the extension packages - it would also require down-leveling OpenTelemetry + the Azure.Monitor exporter +
# Azure.Core (a large dependency-matrix change, and the profiler is compiled against the OTel 1.15 API).
# Until that strategy is decided, only net10.0 is published. Adding "net8.0"/"net9.0" back here (with a
# compatible down-leveled dependency set) is the remaining A2 work; the resolver + layout already support
# it. See plan.md "A2".
$targetFrameworks = @("net10.0")

# Per-TFM overrides for the version PROPERTIES defined in the Directory.Packages.props files. net10.0 uses
# the repo defaults (10.0.0), so no overrides. The net8.0/net9.0 maps below are retained for when the
# down-leveled dependency set is worked out (they are NOT sufficient on their own - see the note above).
$tfmVersionOverrides = @{
    "net8.0" = @{
        "_MicrosoftExtensionsVersion"                 = "8.0.0"
        "_MicrosoftExtensionsOptionsVersion"          = "8.0.2"
        "_SystemTextJsonVersion"                      = "8.0.5"
        "_SystemDiagnosticsDiagnosticSourceVersion"   = "8.0.1"
    }
    "net9.0" = @{
        "_MicrosoftExtensionsVersion"                 = "9.0.0"
        "_MicrosoftExtensionsOptionsVersion"          = "9.0.0"
        "_SystemTextJsonVersion"                      = "9.0.0"
        "_SystemDiagnosticsDiagnosticSourceVersion"   = "9.0.0"
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
