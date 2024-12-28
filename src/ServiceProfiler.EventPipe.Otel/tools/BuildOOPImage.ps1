param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$ImageName = 'monitor-profiler',
    [Parameter(Mandatory)]
    [string]$Version
)

if ($PSVersionTable.PSVersion.Major -ge 7) {
    Write-Host "PowerShell 7 or later is installed."
    Write-Host "Version: $($PSVersionTable.PSVersion)"
}
else {
    Write-Host "PowerShell 7 is not installed."
    Exit
}

New-Variable -Name ProjectName -Value "Azure.Monitor.OpenTelemetry.Profiler.OOPHost.csproj" -Option Constant

$tag = '{0}:{1}' -f $ImageName, $Version
Write-Debug "Tag: $tag"

$SolutionDir = Split-Path -Parent $PSScriptRoot
$ContextDir = Join-Path $SolutionDir "Azure.Monitor.OpenTelemetry.Profiler.OOPHost"


Push-Location $ContextDir
try {
    dotnet restore "$ProjectName"
    dotnet publish "$ProjectName" -c $Configuration -o "Publish" --no-restore

    docker build -t $tag -f dockerfile .
}
finally {
    Pop-Location
}
