param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = "Undefined",
    [switch]$Rebuild
)

$Undefined = "Undefined"

function GetAVersionNumber {
    param (
        [int]$Major = "99"
    )
    
    $rev = [float](Get-Date -Format "HHmmss");
    $rev = $rev % 65536;

    return (Get-Date -Format "$Major.yyyy.MMdd.$rev");
}


Write-Host Target Configuration: $Configuration. ReBuild: $Rebuild

IF ($Configuration -eq $Undefined) {
    Write-Host "Configuration is not set."
    Exit
}

$AssemblyVersion = GetAVersionNumber("99");
Write-Host Assembly Version Number: $AssemblyVersion

Set-Location $PSScriptRoot

$SolutionPath = "$PSScriptRoot\..\..\Azure.Monitor.OpenTelemetry.Profiler.sln"

if($Rebuild)
{
    Write-Host "Clean the solution for rebuilding..."
    Write-Debug "dotnet clean $SolutionPath"
    dotnet clean $SolutionPath
}

dotnet restore $SolutionPath
dotnet build -c $Configuration --no-restore $SolutionPath /p:AssemblyVersion=$AssemblyVersion
