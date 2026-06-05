# create-pr.ps1
# Creates a PR on the upstream repo using content from pr.draft.md.
# Usage: .\create-pr.ps1 [-DraftFile <path>] [-Draft]
#
# pr.draft.md format:
#   Line 1:  # PR title here
#   Lines 2+: PR body (markdown)

param(
    [string]$DraftFile = (Join-Path $PSScriptRoot "pr.draft.md"),
    [switch]$Draft
)

$ErrorActionPreference = 'Stop'
$upstream = "Azure/azuremonitor-opentelemetry-profiler-net"

# --- Resolve fork owner and branch ---
$remoteUrl = git remote get-url origin 2>$null
if (-not $remoteUrl) {
    Write-Host "ERROR: No 'origin' remote found." -ForegroundColor Red
    exit 1
}

if ($remoteUrl -match '[:/]([^/]+)/[^/]+?(?:\.git)?$') {
    $forkOwner = $Matches[1]
} else {
    Write-Host "ERROR: Cannot parse fork owner from remote URL: $remoteUrl" -ForegroundColor Red
    exit 1
}

$branch = git branch --show-current
if (-not $branch) {
    Write-Host "ERROR: Cannot determine current branch." -ForegroundColor Red
    exit 1
}

# --- Read pr.draft.md ---
if (-not (Test-Path $DraftFile)) {
    Write-Host "ERROR: Draft file not found: $DraftFile" -ForegroundColor Red
    Write-Host "Create a pr.draft.md with line 1 as '# Title' and the rest as the body." -ForegroundColor Yellow
    exit 1
}

$lines = Get-Content $DraftFile -Raw
$lines = $lines.TrimStart()

# First line starting with '# ' is the title
if ($lines -match '^#\s+(.+)') {
    $title = $Matches[1].Trim()
    $body = ($lines -replace '^#\s+.+\r?\n', '').Trim()
} else {
    Write-Host "ERROR: First line of $DraftFile must start with '# ' (the PR title)." -ForegroundColor Red
    exit 1
}

# --- Pre-flight: verify gh CLI is installed ---
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: 'gh' CLI not found. Install from https://cli.github.com/" -ForegroundColor Red
    exit 1
}

# --- Pre-flight: verify gh is authenticated ---
$authStatus = gh auth status 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Not logged in to GitHub CLI." -ForegroundColor Red
    Write-Host ""
    Write-Host "Run this to fix:" -ForegroundColor Yellow
    Write-Host "  gh auth login -h github.com -w" -ForegroundColor White
    exit 1
}

# --- Pre-flight: verify SSO access ---
Write-Host "Checking access to $upstream..." -ForegroundColor Cyan
gh api "repos/$upstream" --silent 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Cannot access $upstream. You likely need to authorize SSO." -ForegroundColor Red
    Write-Host ""
    Write-Host "Run this to fix:" -ForegroundColor Yellow
    Write-Host "  gh auth refresh -h github.com -s read:org,repo,workflow" -ForegroundColor White
    Write-Host ""
    Write-Host "Then re-run this script." -ForegroundColor Yellow
    exit 1
}
Write-Host "Auth OK." -ForegroundColor Green

# --- Pre-flight: check branch is not main ---
if ($branch -eq 'main') {
    Write-Host "ERROR: You're on 'main'. Switch to a feature branch first." -ForegroundColor Red
    exit 1
}

# --- Pre-flight: check branch is pushed to remote ---
$forkRepo = "${forkOwner}/$(($remoteUrl -split '/')[-1] -replace '\.git$','')"
gh api "repos/$forkRepo/branches/$branch" --silent 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Branch '$branch' has not been pushed to origin." -ForegroundColor Red
    Write-Host ""
    Write-Host "Run this to fix:" -ForegroundColor Yellow
    Write-Host "  git push -u origin $branch" -ForegroundColor White
    exit 1
}

# --- Pre-flight: check for unpushed commits ---
$trackingBranch = git rev-parse --abbrev-ref "@{upstream}" 2>$null
if ($trackingBranch) {
    $unpushed = git log "${trackingBranch}..HEAD" --oneline 2>$null
    if ($unpushed) {
        Write-Host "WARNING: You have unpushed commits:" -ForegroundColor Yellow
        $unpushed | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        Write-Host ""
        $cont = Read-Host "Continue anyway? (y/N)"
        if ($cont -ne 'y') {
            Write-Host "Push first with: git push" -ForegroundColor Yellow
            exit 0
        }
    }
}

# --- Create PR via REST API (avoids GraphQL SAML issues) ---
Write-Host ""
Write-Host "Creating PR..." -ForegroundColor Cyan
Write-Host "  From: ${forkOwner}:${branch}" -ForegroundColor Gray
Write-Host "  To:   ${upstream} (main)" -ForegroundColor Gray
Write-Host "  Title: $title" -ForegroundColor Gray

$prPayload = @{
    title = $title
    body  = $body
    head  = "${forkOwner}:${branch}"
    base  = "main"
    draft = [bool]$Draft
} | ConvertTo-Json -Depth 3

$resultJson = $prPayload | gh api "repos/$upstream/pulls" --input - 2>&1
if ($LASTEXITCODE -ne 0) {
    # Check if a PR already exists for this head branch
    if ($resultJson -match 'A pull request already exists') {
        Write-Host ""
        Write-Host "A PR already exists for ${forkOwner}:${branch}." -ForegroundColor Yellow

        # Find the existing PR
        $existingJson = gh api "repos/$upstream/pulls?head=${forkOwner}:${branch}&state=open" 2>$null
        $existing = ($existingJson | ConvertFrom-Json)
        if ($existing.Count -gt 0) {
            $pr = $existing[0]
            Write-Host ""
            Write-Host "  PR #$($pr.number): $($pr.title)"
            Write-Host "  URL: $($pr.html_url)" -ForegroundColor Cyan
            Write-Host ""

            $update = Read-Host "Update title and body from pr.draft.md? (y/N)"
            if ($update -eq 'y') {
                $updatePayload = @{
                    title = $title
                    body  = $body
                } | ConvertTo-Json -Depth 3

                $updateResult = $updatePayload | gh api "repos/$upstream/pulls/$($pr.number)" --method PATCH --input - 2>&1
                if ($LASTEXITCODE -ne 0) {
                    Write-Host "ERROR: Failed to update PR." -ForegroundColor Red
                    Write-Host $updateResult -ForegroundColor Red
                    exit 1
                }

                Write-Host ""
                Write-Host "============================================" -ForegroundColor Green
                Write-Host " PR #$($pr.number) updated successfully!" -ForegroundColor Green
                Write-Host "============================================" -ForegroundColor Green
                Write-Host ""
                Write-Host "  URL: $($pr.html_url)" -ForegroundColor Cyan
            } else {
                Write-Host "No changes made." -ForegroundColor Gray
            }
        }
        exit 0
    }

    Write-Host "ERROR: Failed to create PR." -ForegroundColor Red

    if ($resultJson -match 'No commits between') {
        Write-Host "Your branch has no new commits compared to main." -ForegroundColor Yellow
        Write-Host "Make sure you have commits ahead of upstream/main." -ForegroundColor Yellow
    } elseif ($resultJson -match 'permission|403|Forbidden') {
        Write-Host "You don't have permission to create PRs on $upstream." -ForegroundColor Yellow
        Write-Host "Make sure your fork is public and your token has 'repo' scope." -ForegroundColor Yellow
    } elseif ($resultJson -match 'rate limit|429') {
        Write-Host "GitHub API rate limit exceeded. Wait a few minutes and try again." -ForegroundColor Yellow
    } elseif ($resultJson -match 'Not Found|404') {
        Write-Host "Repository not found. Check that '$upstream' exists and is accessible." -ForegroundColor Yellow
    } else {
        Write-Host $resultJson -ForegroundColor Red
    }
    exit 1
}

$pr = $resultJson | ConvertFrom-Json

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host " PR #$($pr.number) created successfully!" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Title:   $($pr.title)"
Write-Host "  URL:     $($pr.html_url)" -ForegroundColor Cyan
Write-Host "  State:   $($pr.state)"
Write-Host "  Draft:   $($pr.draft)"
Write-Host "  Head:    $($pr.head.label)"
Write-Host "  Base:    $($pr.base.label)"
Write-Host ""
