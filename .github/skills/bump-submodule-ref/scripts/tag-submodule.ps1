# Tags the ServiceProfiler submodule with otel-profiler-ref-<short-hash> and pushes the tag.
# Usage: .\tag-submodule.ps1 [-RepoRoot <path>]
# Defaults to current directory if RepoRoot is not provided.

param(
    [string]$RepoRoot = "."
)

$ErrorActionPreference = "Stop"
$SubmoduleDir = Join-Path $RepoRoot "ServiceProfiler"

if (-not (Test-Path (Join-Path $SubmoduleDir ".git"))) {
    Write-Error "ServiceProfiler submodule not found at $SubmoduleDir"
    exit 1
}

# Check submodule is clean
$status = git -C $SubmoduleDir status --porcelain
if ($status) {
    Write-Error "ServiceProfiler has uncommitted changes. Commit or stash them first."
    exit 1
}

$ShortHash = git -C $SubmoduleDir rev-parse --short HEAD
$TagName = "otel-profiler-ref-$ShortHash"

Write-Host "Submodule HEAD: $(git -C $SubmoduleDir log --oneline -1)"
Write-Host "Tag: $TagName"

# Check if tag already exists
$existing = git -C $SubmoduleDir rev-parse $TagName 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Tag $TagName already exists. Skipping."
    exit 0
}

git -C $SubmoduleDir tag $TagName
Write-Host "Tag $TagName created."

git -C $SubmoduleDir push origin $TagName
Write-Host "Tag $TagName pushed to origin."
