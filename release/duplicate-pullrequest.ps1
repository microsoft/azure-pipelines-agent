<#
.SYNOPSIS
    Duplicates an existing GitHub Pull Request

.DESCRIPTION
    Creates a new PR with the same title, body, and file changes as an existing PR.
    This script uses the GitHub CLI (gh) to fetch PR details and create a new PR.

.PARAMETER PrNumber
    The number of the PR to duplicate

.PARAMETER NewBranchName
    Optional. Name for the new branch. If not provided, defaults to "duplicate-pr-{number}"

.PARAMETER DryRun
    Optional. If set, shows what would be done without actually creating the PR

.EXAMPLE
    .\duplicate-pullrequest.ps1 -PrNumber 1234

.EXAMPLE
    .\duplicate-pullrequest.ps1 -PrNumber 1234 -NewBranchName "my-duplicate-branch"

.EXAMPLE
    .\duplicate-pullrequest.ps1 -PrNumber 1234 -DryRun
#>

param(
    [Parameter(Mandatory=$true)]
    [int]$PrNumber,
    
    [Parameter(Mandatory=$false)]
    [string]$NewBranchName = "",
    
    [Parameter(Mandatory=$false)]
    [switch]$DryRun
)

# Set error action preference
$ErrorActionPreference = "Stop"

# Function to execute git commands
function Invoke-Git {
    param(
        [string]$Arguments,
        [switch]$Silent
    )
    
    if (-not $Silent) {
        Write-Host "Executing: git $Arguments" -ForegroundColor Cyan
    }
    
    $output = & git $Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed: git $Arguments`n$output"
    }
    return $output
}

# Function to get PR details
function Get-PRDetails {
    param([int]$Number)
    
    Write-Host "Fetching details for PR #$Number..." -ForegroundColor Yellow
    $json = gh pr view $Number --json number,title,body,headRefName,baseRefName,state,files
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to fetch PR details"
    }
    return $json | ConvertFrom-Json
}

# Function to get PR diff
function Get-PRDiff {
    param([int]$Number)
    
    Write-Host "Fetching diff for PR #$Number..." -ForegroundColor Yellow
    $diff = gh pr diff $Number
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to fetch PR diff"
    }
    return $diff
}

# Main script
try {
    Write-Host "=== Duplicating PR #$PrNumber ===" -ForegroundColor Green
    
    # Set default branch name if not provided
    if ([string]::IsNullOrEmpty($NewBranchName)) {
        $NewBranchName = "duplicate-pr-$PrNumber"
    }
    
    Write-Host "Dry run: $DryRun" -ForegroundColor Cyan
    Write-Host "New branch name: $NewBranchName" -ForegroundColor Cyan
    Write-Host ""
    
    # Get PR details
    $prDetails = Get-PRDetails -Number $PrNumber
    
    Write-Host "PR Details:" -ForegroundColor Yellow
    Write-Host "  Title: $($prDetails.title)"
    Write-Host "  Base: $($prDetails.baseRefName)"
    Write-Host "  Head: $($prDetails.headRefName)"
    Write-Host "  State: $($prDetails.state)"
    Write-Host "  Files changed: $($prDetails.files.Count)"
    Write-Host ""
    
    if ($prDetails.state -ne "OPEN" -and $prDetails.state -ne "CLOSED") {
        Write-Warning "PR is in state $($prDetails.state)"
    }
    
    # Get the diff
    $diff = Get-PRDiff -Number $PrNumber
    
    if ($DryRun) {
        Write-Host "=== DRY RUN MODE ===" -ForegroundColor Magenta
        Write-Host "Would create branch: $NewBranchName"
        Write-Host "Would apply patch with $($diff.Split("`n").Count) lines"
        Write-Host "Would commit with message: $($prDetails.title)"
        Write-Host "Would push branch: $NewBranchName"
        Write-Host "Would create PR with:"
        Write-Host "  Title: $($prDetails.title)"
        Write-Host "  Base: $($prDetails.baseRefName)"
        Write-Host "  Body: $(if ($prDetails.body) { $prDetails.body } else { '(empty)' })"
        exit 0
    }
    
    # Check for uncommitted changes
    $status = Invoke-Git -Arguments "status --porcelain" -Silent
    if ($status) {
        throw "You have uncommitted changes. Please commit or stash them first."
    }
    
    # Checkout base branch and pull latest
    Write-Host "Checking out base branch: $($prDetails.baseRefName)" -ForegroundColor Yellow
    Invoke-Git -Arguments "checkout $($prDetails.baseRefName)"
    Invoke-Git -Arguments "pull"
    
    # Create new branch
    Write-Host "Creating new branch: $NewBranchName" -ForegroundColor Yellow
    Invoke-Git -Arguments "checkout -b $NewBranchName"
    
    # Apply the patch
    Write-Host "Applying patch to new branch..." -ForegroundColor Yellow
    $tempPatchFile = Join-Path $env:TEMP "pr-patch-$(Get-Date -Format 'yyyyMMddHHmmss').patch"
    $diff | Out-File -FilePath $tempPatchFile -Encoding UTF8
    
    try {
        Invoke-Git -Arguments "apply $tempPatchFile"
        Write-Host "Patch applied successfully" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to apply patch: $_"
        throw
    }
    finally {
        if (Test-Path $tempPatchFile) {
            Remove-Item $tempPatchFile -Force
        }
    }
    
    # Stage and commit changes
    Write-Host "Staging changes..." -ForegroundColor Yellow
    Invoke-Git -Arguments "add ."
    
    Write-Host "Committing changes..." -ForegroundColor Yellow
    Invoke-Git -Arguments "commit -m `"$($prDetails.title)`""
    
    # Push branch
    Write-Host "Pushing branch $NewBranchName to remote..." -ForegroundColor Yellow
    Invoke-Git -Arguments "push -u origin $NewBranchName"
    
    # Create PR
    Write-Host "Creating new pull request..." -ForegroundColor Yellow
    $newBody = "This PR is a duplicate of #$PrNumber`n`n---`n`n$($prDetails.body)"
    
    gh pr create --title "$($prDetails.title)" --body "$newBody" --base "$($prDetails.baseRefName)"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create PR"
    }
    
    Write-Host ""
    Write-Host "=== SUCCESS ===" -ForegroundColor Green
    Write-Host "Successfully created a duplicate of PR #$PrNumber" -ForegroundColor Green
    Write-Host "New branch: $NewBranchName" -ForegroundColor Green
}
catch {
    Write-Error "Error: $_"
    exit 1
}
