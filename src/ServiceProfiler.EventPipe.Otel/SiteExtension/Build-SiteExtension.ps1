<#
.SYNOPSIS
    Builds the codeless Azure Monitor OpenTelemetry Profiler Site Extension (.nupkg) for Windows App Service.

.DESCRIPTION
    Produces a staging layout:
        staging/
          applicationHost.xdt
          payload/
            Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll   (assembly-resolution bootstrap)
            Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.dll (detect + route profiler)
            <all profiler + dependency assemblies>
            Uploader/
              Microsoft.ApplicationInsights.Profiler.Uploader.dll   (out-of-proc trace uploader)
    then packs it into Out/NuGets/Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.<version>.nupkg
    with the AzureSiteExtension tag so it appears in the Kudu Site Extensions gallery.

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
$otelDir   = Split-Path -Parent $here
$srcDir    = Split-Path -Parent $otelDir
$repoRoot  = Split-Path -Parent $srcDir

$hostingStartupProj = Join-Path $otelDir "Azure.Monitor.OpenTelemetry.Profiler.HostingStartup\Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.csproj"
$startupHookProj    = Join-Path $otelDir "Azure.Monitor.OpenTelemetry.Profiler.StartupHook\Azure.Monitor.OpenTelemetry.Profiler.StartupHook.csproj"
$uploaderProj       = Join-Path $srcDir  "ServiceProfiler.EventPipe.Upload\ServiceProfiler.EventPipe.Upload.csproj"
$nuspec             = Join-Path $here    "Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.nuspec"

$staging     = Join-Path $here "staging"
$payload     = Join-Path $staging "payload"
$uploaderOut = Join-Path $payload "Uploader"
$outDir      = Join-Path $repoRoot "Out\NuGets"

$targetFramework = "net8.0"

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
New-Item -ItemType Directory -Force -Path $payload | Out-Null
New-Item -ItemType Directory -Force -Path $outDir  | Out-Null

# 2. Publish the HostingStartup closure (brings both profiler stacks + all dependencies).
Invoke-Checked "Publishing HostingStartup payload ($targetFramework)" {
    dotnet publish $hostingStartupProj -c $Configuration -f $targetFramework -o $payload --nologo
}

# 3. Build the resolver StartupHook and copy just its single (dependency-free) assembly into the payload.
Invoke-Checked "Building StartupHook resolver" {
    dotnet build $startupHookProj -c $Configuration -f $targetFramework --nologo
}
$startupHookDll = Join-Path $otelDir "Azure.Monitor.OpenTelemetry.Profiler.StartupHook\bin\$Configuration\$targetFramework\Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll"
if (-not (Test-Path $startupHookDll)) { throw "StartupHook assembly not found at $startupHookDll." }
Copy-Item $startupHookDll -Destination $payload -Force

# 4. Publish the out-of-proc uploader into payload/Uploader.
Invoke-Checked "Publishing the trace uploader ($targetFramework)" {
    dotnet publish $uploaderProj -c $Configuration -f $targetFramework -o $uploaderOut --nologo
}

# 5. Copy the applicationHost.xdt into the staging root.
Copy-Item (Join-Path $here "applicationHost.xdt") -Destination $staging -Force

# 6. Pack the site extension.
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
