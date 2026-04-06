# Tags a specific commit and pushes the tag to remotes.
# Usage: .\tag-and-push.ps1 -TagName <version> -CommitHash <sha> [-Remote <remote>]
# Examples:
#   .\tag-and-push.ps1 -TagName 1.0.0-beta3 -CommitHash abc1234
#   .\tag-and-push.ps1 -TagName 1.0.0 -CommitHash abc1234 -Remote internal

param(
    [Parameter(Mandatory = $true)]
    [string]$TagName,

    [Parameter(Mandatory = $true)]
    [string]$CommitHash,

    [ValidateSet("origin", "internal")]
    [string]$Remote = "origin"
)

$ErrorActionPreference = "Stop"

# Validate version-like tag name
if ($TagName -notmatch '^\d+\.\d+\.\d+') {
    Write-Error "TagName '$TagName' does not look like a version (expected format: 1.0.0, 1.0.0-beta3, etc.)"
    exit 1
}

# Validate the commit exists
$resolved = git rev-parse --verify "$CommitHash^{commit}" 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Error "Commit '$CommitHash' not found in the repository."
    exit 1
}

Write-Host "Commit : $resolved ($(git log --oneline -1 $resolved))"
Write-Host "Tag    : $TagName"
Write-Host "Remote : $Remote"

# Check if tag already exists locally
$existing = git rev-parse $TagName 2>$null
if ($LASTEXITCODE -eq 0) {
    if ($existing -eq $resolved) {
        Write-Host "Tag '$TagName' already exists on the correct commit. Skipping creation."
    } else {
        Write-Error "Tag '$TagName' already exists but points to a different commit ($existing). Delete it first if you want to re-tag."
        exit 1
    }
} else {
    git tag $TagName $resolved
    Write-Host "Tag '$TagName' created on commit $resolved."
}

# Push to the specified remote
git push $Remote $TagName
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to push tag '$TagName' to '$Remote'."
    exit 1
}
Write-Host "Tag '$TagName' pushed to '$Remote'."
