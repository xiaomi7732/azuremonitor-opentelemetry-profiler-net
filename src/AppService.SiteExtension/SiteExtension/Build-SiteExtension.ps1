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
$payload     = Join-Path $staging "payload\$Version"
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
$startupHookDll = Join-Path $extRoot "Azure.Monitor.OpenTelemetry.Profiler.StartupHook\bin\$Configuration\$targetFramework\Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll"
if (-not (Test-Path $startupHookDll)) { throw "StartupHook assembly not found at $startupHookDll." }
Copy-Item $startupHookDll -Destination $payload -Force

# 4. Publish the out-of-proc uploader into payload/Uploader.
Invoke-Checked "Publishing the trace uploader ($targetFramework)" {
    dotnet publish $uploaderProj -c $Configuration -f $targetFramework -o $uploaderOut --nologo
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
