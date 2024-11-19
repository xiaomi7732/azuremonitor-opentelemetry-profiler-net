param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration='Release',
    [string]$PackageType = "private",
    [switch]$Rebuild
)

Write-Host "Target Configuration: $Configuration. ReBuild: $Rebuild. Package Type: $PackageType."

$VersionSuffixAddon = Get-Date -Format "yyMMddHHmm"
$VersionSuffix = "$PackageType$VersionSuffixAddon"

if ($Configuration -eq 'Debug') {
    $VersionSuffix = $VersionSuffix + "debug"
}

$BaseDir = Split-Path -Parent $PSScriptRoot
$SolutionDir = Split-Path -Parent $BaseDir
$OutputDir = Join-Path -Path (Split-Path -Parent $SolutionDir) -ChildPath "Out"
$NuGetOutDir = Join-Path -Path $OutputDir -ChildPath "NuGets"

Write-Host "Prepare Output Folder: $OutputDir"

New-Item -ItemType Directory -Path $OutputDir -Force
New-Item -ItemType Directory -Path $NuGetOutDir -Force

Remove-Item $BaseDir\Azure.Monitor.OpenTelemetry.Profiler.Core\bin\$Configuration\*.nupkg -Force
Remove-Item $BaseDir\Azure.Monitor.OpenTelemetry.Profiler.AspNetCore\bin\$Configuration\*.nupkg -Force

Write-Host Build the solution
& $PSScriptRoot\BuildSolution.ps1 $Configuration -Rebuild:$Rebuild

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed building the solution."
    EXIT -100
}

Write-Host "Pack nuget packages"
dotnet pack $BaseDir\Azure.Monitor.OpenTelemetry.Profiler.Core --no-build --no-restore --version-suffix $VersionSuffix -c $Configuration
dotnet pack $BaseDir\Azure.Monitor.OpenTelemetry.Profiler.AspNetCore --no-build --no-restore --version-suffix $VersionSuffix -c $Configuration

XCOPY $BaseDir\Azure.Monitor.OpenTelemetry.Profiler.Core\bin\$Configuration\*.nupkg $NuGetOutDir /y /f
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed copying the NuGet package file"
    EXIT -200
}

XCOPY $BaseDir\Azure.Monitor.OpenTelemetry.Profiler.AspNetCore\bin\$Configuration\*.nupkg $NuGetOutDir /y /f
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed copying the NuGet package file"
    EXIT -200
}

Write-Host "Package succeeded :-)"
