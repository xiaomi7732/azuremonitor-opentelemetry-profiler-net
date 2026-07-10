<#
.SYNOPSIS
    Codeless-enable the Azure Monitor Profiler on a Linux Azure App Service (blessed .NET stack).

.DESCRIPTION
    Linux App Service has no Kudu site-extension gallery and no applicationHost.xdt, so this script does what
    the Windows site extension does, manually: it stages the portable payload under the app's persistent
    /home and sets the three injection environment variables as App Settings (append + de-duplicate, so it
    coexists with the platform Application Insights agent or user-set hooks). Setting App Settings restarts the
    app, after which the profiler activates codelessly.

    This is the cross-platform PowerShell (pwsh) equivalent of enable-linux-appservice.sh; pwsh runs on
    Linux/macOS/Windows, so this script is NOT Windows-only.

    Dependencies (no .NET SDK required to RUN this):
      - Azure CLI (az), logged in:  az login
      - PowerShell 7+ (pwsh)
    The payload zip is produced by the repo build (Build-SiteExtension.ps1 -> Out/Linux/AzureMonitorProfiler.<version>.zip).

.EXAMPLE
    pwsh ./Enable-LinuxAppService.ps1 -ResourceGroup my-rg -Name my-app
.EXAMPLE
    pwsh ./Enable-LinuxAppService.ps1 -ResourceGroup my-rg -Name my-app -Disable
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ResourceGroup,
    [Parameter(Mandatory)][string]$Name,
    [string]$Slot,
    [string]$PayloadZip,
    [string]$PayloadVersion,
    [switch]$DebugLog,
    [switch]$Disable
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$HostingStartupAssembly = "Azure.Monitor.OpenTelemetry.Profiler.HostingStartup"
$RemoteBase = "/home/AzureMonitorProfiler"

function Die([string]$m) { Write-Error $m; exit 1 }
if (-not (Get-Command az -ErrorAction SilentlyContinue)) { Die "Azure CLI 'az' not found. Install it and run 'az login'." }
& az account show 1>$null 2>$null; if ($LASTEXITCODE -ne 0) { Die "Not logged in. Run 'az login' (and 'az account set --subscription <id>')." }

$slotArgs = @(); if ($Slot) { $slotArgs = @('--slot', $Slot) }

# Append + de-duplicate a value into a semicolon-separated list (mirrors the Windows AppendListValueIfMissing XDT).
function Append-Dedup([string]$current, [string]$value) {
    $items = @()
    if ($current) { $items = @($current.Split(';') | Where-Object { $_ -and $_ -ne $value }) }
    return ((@($items) + $value) -join ';')
}
function Remove-FromList([string]$current, [string]$value) {
    if (-not $current) { return '' }
    return (@($current.Split(';') | Where-Object { $_ -and $_ -ne $value }) -join ';')
}
# Append our versioned StartupHook path, removing ANY prior version of our hook first (upgrades stage into a
# new /home/AzureMonitorProfiler/<version>/ folder and App Settings persist, so a plain append+dedup would
# leave the old versioned hook in DOTNET_STARTUP_HOOKS and it would run first and load the stale payload).
function Append-HookReplacingPrior([string]$current, [string]$newPath) {
    $items = @()
    if ($current) {
        $items = @($current.Split(';') | Where-Object {
            $_ -and $_ -ne $newPath -and
            $_ -notmatch '[/\\]AzureMonitorProfiler[/\\].*[/\\]Azure\.Monitor\.OpenTelemetry\.Profiler\.StartupHook\.dll$'
        })
    }
    return ((@($items) + $newPath) -join ';')
}
function Get-Setting([string]$name) {
    return (& az webapp config appsettings list -g $ResourceGroup -n $Name @slotArgs --query "[?name=='$name'].value | [0]" -o tsv)
}

$curHooks = Get-Setting 'DOTNET_STARTUP_HOOKS'
$curHsa   = Get-Setting 'ASPNETCORE_HOSTINGSTARTUPASSEMBLIES'

if ($Disable) {
    Write-Host "Disabling the codeless profiler on $Name$(if($Slot){" (slot: $Slot)"})..."
    $ourHook = $null
    if ($curHooks) { $ourHook = $curHooks.Split(';') | Where-Object { $_ -match "$RemoteBase/.*/Azure\.Monitor\.OpenTelemetry\.Profiler\.StartupHook\.dll$" } | Select-Object -First 1 }
    $newHooks = if ($ourHook) { Remove-FromList $curHooks $ourHook } else { $curHooks }
    $newHsa   = Remove-FromList $curHsa $HostingStartupAssembly
    $set = @()
    if ($newHooks) { $set += "DOTNET_STARTUP_HOOKS=$newHooks" } else { & az webapp config appsettings delete -g $ResourceGroup -n $Name @slotArgs --setting-names DOTNET_STARTUP_HOOKS 1>$null }
    if ($newHsa)   { $set += "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=$newHsa" } else { & az webapp config appsettings delete -g $ResourceGroup -n $Name @slotArgs --setting-names ASPNETCORE_HOSTINGSTARTUPASSEMBLIES 1>$null }
    & az webapp config appsettings delete -g $ResourceGroup -n $Name @slotArgs --setting-names SP_UPLOADER_PATH SP_STARTUP_LOG 1>$null 2>$null
    if ($set.Count -gt 0) { & az webapp config appsettings set -g $ResourceGroup -n $Name @slotArgs --settings $set 1>$null }
    Write-Host "Disabled. The staged payload under $RemoteBase was left in place (delete manually via Kudu if desired)."
    exit 0
}

# Resolve the payload zip and version.
if (-not $PayloadZip) {
    $candidate = Join-Path $PSScriptRoot "..\..\..\Out\Linux"
    $PayloadZip = (Get-ChildItem -Path $candidate -Filter "AzureMonitorProfiler.*.zip" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName)
}
if (-not $PayloadZip -or -not (Test-Path $PayloadZip)) { Die "Payload zip not found. Build it (Build-SiteExtension.ps1) or pass -PayloadZip <path>." }
if (-not $PayloadVersion) {
    $b = Split-Path $PayloadZip -Leaf
    $PayloadVersion = $b -replace '^AzureMonitorProfiler\.', '' -replace '\.zip$', ''
}
if (-not $PayloadVersion) { Die "Could not determine payload version; pass -PayloadVersion <version>." }

$remoteDir     = "$RemoteBase/$PayloadVersion"
$hookPath      = "$remoteDir/Azure.Monitor.OpenTelemetry.Profiler.StartupHook.dll"
$uploaderPath  = "$remoteDir/Uploader/Microsoft.ApplicationInsights.Profiler.Uploader.dll"

# Resolve SCM host + AAD token (works even when SCM basic auth is disabled).
$defaultHost = & az webapp show -g $ResourceGroup -n $Name @slotArgs --query defaultHostName -o tsv
if ($LASTEXITCODE -ne 0 -or -not $defaultHost) { Die "Could not find app '$Name' in resource group '$ResourceGroup'." }
$scmHost = $defaultHost -replace '^([^.]+)\.', '$1.scm.'
$token = & az account get-access-token --resource https://management.azure.com --query accessToken -o tsv

Write-Host "Staging payload to $remoteDir on $Name$(if($Slot){" (slot: $Slot)"}) ..."
Write-Host "  zip: $PayloadZip"
try {
    Invoke-RestMethod -Method Put -Uri "https://$scmHost/api/zip$remoteDir/" `
        -Headers @{ Authorization = "Bearer $token" } -InFile $PayloadZip -ContentType 'application/zip' | Out-Null
} catch { Die "Kudu zip upload failed: $($_.Exception.Message)" }
Write-Host "  staged."

$newHooks = Append-HookReplacingPrior $curHooks $hookPath
$newHsa   = Append-Dedup $curHsa $HostingStartupAssembly

Write-Host "Setting App Settings (this restarts the app)..."
$settings = @(
    "DOTNET_STARTUP_HOOKS=$newHooks",
    "ASPNETCORE_HOSTINGSTARTUPASSEMBLIES=$newHsa",
    "SP_UPLOADER_PATH=$uploaderPath"
)
if ($DebugLog) { $settings += "SP_STARTUP_LOG=1" }
& az webapp config appsettings set -g $ResourceGroup -n $Name @slotArgs --settings $settings 1>$null

Write-Host ""
Write-Host "Done. The codeless profiler is enabled on '$Name'$(if($Slot){" (slot: $Slot)"})." -ForegroundColor Green
Write-Host "  DOTNET_STARTUP_HOOKS                 -> $hookPath"
Write-Host "  ASPNETCORE_HOSTINGSTARTUPASSEMBLIES  -> (…;) $HostingStartupAssembly"
Write-Host "  SP_UPLOADER_PATH                     -> $uploaderPath"
Write-Host ""
Write-Host "Ensure APPLICATIONINSIGHTS_CONNECTION_STRING is set on the app, then check the log stream for the"
Write-Host "StartupHook/HostingStartup activation lines. To remove: re-run with -Disable."
