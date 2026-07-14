<#
.SYNOPSIS
    Builds the codeless Azure Monitor OpenTelemetry Profiler Site Extension (.nupkg) for Windows App Service.

.DESCRIPTION
    Produces a staging layout:
        staging/
          applicationHost.xdt
          payload/
            <version>/
              Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll   (detect + scope resolver per stack)
              Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.dll (stack-agnostic router)
              otel/
                Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.OpenTelemetryActivator.dll
                <OpenTelemetry profiler closure - its OWN dependency versions>
              classic/
                Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.ClassicActivator.dll
                <classic profiler closure - its OWN dependency versions>
              Uploader/
                Microsoft.ApplicationInsights.Profiler.Uploader.dll   (out-of-proc trace uploader, SHARED)
    then packs it into Out/NuGets/Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.<version>.nupkg
    with the AzureSiteExtension tag so it appears in the Kudu Site Extensions gallery.

    Each profiler stack is published into its OWN subfolder (otel\ / classic\) by a SEPARATE dotnet publish,
    so the two stacks' dependency closures are never unified into a single shared version. Only one stack
    activates at runtime: the StartupHook detects the app's telemetry stack, records the decision, and scopes
    the assembly resolver to that stack's subfolder only. The uploader is shared (both stacks locate it via
    %SP_UPLOADER_PATH%).

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
    Package version. Default: 1.0.0-beta.1.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0-beta.1"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$here      = $PSScriptRoot
$extRoot   = Split-Path -Parent $here
$srcDir    = Split-Path -Parent $extRoot
$repoRoot  = Split-Path -Parent $srcDir

$hostingStartupProj = Join-Path $extRoot "Azure.Monitor.OpenTelemetry.Profiler.HostingStartup\Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.csproj"
$otelActivatorProj    = Join-Path $extRoot "Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.OpenTelemetryActivator\Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.OpenTelemetryActivator.csproj"
$classicActivatorProj = Join-Path $extRoot "Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.ClassicActivator\Azure.Monitor.OpenTelemetry.Profiler.HostingStartup.ClassicActivator.csproj"
$startupHookProj    = Join-Path $extRoot "Azure.Monitor.OpenTelemetry.Profiler.StartupHook\Azure.Monitor.OpenTelemetry.Profiler.StartupHook.csproj"
$xdtTransformsProj  = Join-Path $extRoot "Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms\Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.XdtTransforms.csproj"
$uploaderProj       = Join-Path $srcDir  "ServiceProfiler.EventPipe.Upload\ServiceProfiler.EventPipe.Upload.csproj"
$nuspec             = Join-Path $here    "Azure.Monitor.OpenTelemetry.Profiler.SiteExtension.nuspec"

$staging     = Join-Path $here "staging"
$payloadRoot = Join-Path $staging "payload\$Version"
$otelOut     = Join-Path $payloadRoot "otel"
$classicOut  = Join-Path $payloadRoot "classic"
$uploaderOut = Join-Path $payloadRoot "Uploader"
$outDir      = Join-Path $repoRoot "Out\NuGets"

# Uploader is a standalone out-of-proc process, independent of the profiled app's runtime.
$uploaderFramework = "net8.0"

# LOW-BASELINE SINGLE PAYLOAD (see plan.md "A2c").
# The codeless profiler is injected into a running app whose shared-framework Microsoft.Extensions.* (and,
# for OTel apps, its own OpenTelemetry / Azure.Core) are already loaded BEFORE our HostingStartup runs. The
# runtime can only roll an already-loaded assembly FORWARD (a higher loaded version satisfies a lower
# reference), never backward. So the profiler must reference the LOWEST version we support; those refs then
# roll forward to whatever the target app loaded. We therefore build ONE payload targeting net8.0 against an
# 8.0 dependency baseline (OpenTelemetry 1.8.1 -> its netstandard asset floors Microsoft.Extensions.* at
# 8.0; exporter 1.3.0 is the version that permits OTel 1.8.1). A net8.0 assembly + 8.0 refs run on .NET 8
# (exact), .NET 9 and .NET 10 (roll-forward) - one folder, no per-runtime variants.
#
# Requirement this imposes on target apps: OpenTelemetry >= 1.8.1 and Azure.Core >= ~1.46 (documented).
$baselineFramework = "net8.0"

# Version-property overrides for the low baseline. Defaults are unchanged for the shipped profiler NuGet;
# these only apply to this site-extension publish. The *.Abstractions packages need higher servicing patches
# than their impls (capped at 8.0.1) due to transitive floors, so they are pinned independently (build-time
# floors only; the app's 8.0.x framework satisfies them at runtime).
$baselineOverrides = @{
    "_AzureMonitorOpenTelemetryExporterVersion"                  = "1.3.0"
    "_OpenTelemetryVersion"                                      = "1.8.1"
    "_MicrosoftExtensionsVersion"                                = "8.0.0"
    "_MicrosoftExtensionsOptionsVersion"                         = "8.0.2"
    "_MicrosoftExtensionsLoggingAbstractionsVersion"             = "8.0.3"
    "_MicrosoftExtensionsDependencyInjectionAbstractionsVersion" = "8.0.2"
    "_SystemTextJsonVersion"                                     = "8.0.6"
    "_SystemDiagnosticsDiagnosticSourceVersion"                  = "8.0.1"
    "_SystemMemoryDataVersion"                                   = "8.0.1"
    "_SystemSecurityCryptographyProtectedDataVersion"           = "8.0.0"
    "_SystemTextEncodingsWebVersion"                             = "8.0.0"
    "_SystemIOHashingVersion"                                    = "8.0.0"
    # Azure.Core is a direct reference of the OpenTelemetry profiler; down-level it too so the payload's
    # floor matches the documented minimum (>= 1.46.1). 1.46.1 is the lowest the graph allows - Azure.Identity
    # 1.14.0 floors Azure.Core at 1.46.1. Without this the payload would ship the repo-default 1.50, silently
    # raising the real floor above what the README documents and breaking apps on Azure.Core 1.46-1.49.
    "_AzureCoreVersion"                                          = "1.46.1"
}
$baselineArgs = @()
foreach ($kvp in $baselineOverrides.GetEnumerator()) { $baselineArgs += "-p:$($kvp.Key)=$($kvp.Value)" }

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

# 2. Publish the stack-agnostic router (net8.0, low baseline) flat into payload\<version>\, then publish each
#    profiler stack into its OWN subfolder (otel\ / classic\) via a SEPARATE dotnet publish so the two stacks'
#    dependency closures are resolved independently and never unified into one shared version. Clean bin/obj
#    first so the shared netstandard2.1 profiler projects recompile against the 8.0 baseline (they may have
#    been built against the repo-default 10.x by another build).
Write-Host "==> Cleaning src bin/obj for a clean low-baseline build" -ForegroundColor Cyan
dotnet build-server shutdown 2>&1 | Out-Null
Get-ChildItem -Recurse -Directory -Path $srcDir -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq 'bin' -or $_.Name -eq 'obj' } |
    ForEach-Object { Remove-Item -Recurse -Force $_.FullName -ErrorAction SilentlyContinue }
Invoke-Checked "Publishing HostingStartup router ($baselineFramework, 8.0 baseline)" {
    dotnet publish $hostingStartupProj -c $Configuration -f $baselineFramework -o $payloadRoot --nologo @baselineArgs
}
Invoke-Checked "Publishing OpenTelemetry activator + closure into otel\ ($baselineFramework, 8.0 baseline)" {
    dotnet publish $otelActivatorProj -c $Configuration -f $baselineFramework -o $otelOut --nologo @baselineArgs
}
Invoke-Checked "Publishing classic activator + closure into classic\ ($baselineFramework, 8.0 baseline)" {
    dotnet publish $classicActivatorProj -c $Configuration -f $baselineFramework -o $classicOut --nologo @baselineArgs
}

# 3. Build the resolver StartupHook (BCL-only) and copy its single assembly next to the payload it resolves.
Invoke-Checked "Building StartupHook resolver" {
    dotnet build $startupHookProj -c $Configuration -f $baselineFramework --nologo
}
$startupHookDll = Join-Path $extRoot "Azure.Monitor.OpenTelemetry.Profiler.StartupHook\bin\$Configuration\$baselineFramework\Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll"
if (-not (Test-Path $startupHookDll)) { throw "StartupHook assembly not found at $startupHookDll." }
Copy-Item $startupHookDll -Destination $payloadRoot -Force

# 4. Publish the out-of-proc uploader once into payload\<version>\Uploader.
Invoke-Checked "Publishing the trace uploader ($baselineFramework)" {
    dotnet publish $uploaderProj -c $Configuration -f $baselineFramework -o $uploaderOut --nologo
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

# 8. Emit a portable Linux payload zip (Linux App Service delivery). Linux has no Kudu site-extension gallery
#    and no applicationHost.xdt, so the enable script stages this zip under /home and sets the injection env
#    vars as App Settings. The zip contains the SAME portable payload (StartupHook + router at root, otel\,
#    classic\, shared Uploader\) - i.e. the CONTENTS of payload\<version>\ - WITHOUT applicationHost.xdt or the
#    XDT transform (those are Windows-only and live at the staging root, not inside payload\). Extracting this
#    zip into /home/AzureMonitorProfiler/<version>/ places the assemblies directly there.
$linuxOutDir = Join-Path $repoRoot "Out\Linux"
New-Item -ItemType Directory -Force -Path $linuxOutDir | Out-Null
$linuxZip = Join-Path $linuxOutDir "AzureMonitorProfiler.$Version.zip"
if (Test-Path $linuxZip) { Remove-Item -Force $linuxZip }
# Use ZipFile.CreateFromDirectory (not Compress-Archive) so entry paths always use '/' separators. Windows
# PowerShell 5.1's Compress-Archive writes '\' entry names, which Kudu's .NET extraction on Linux treats as
# literal flat filenames - that would flatten otel/ classic/ Uploader/ and break the per-stack resolver.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($payloadRoot, $linuxZip)
Write-Host ""
Write-Host "Linux payload zip created:" -ForegroundColor Green
Write-Host "  $linuxZip"
