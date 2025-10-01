param(
    [Parameter(Mandatory)]
    [string]
    $SourcePRNumber
) 

# Get PR details
function Get-PullRequest {
    param([int]$PRNumber)
    return (gh pr view $PRNumber --json number,headRefName,baseRefName,title,body,labels | ConvertFrom-Json)
}

# Get the original PR details
$originalPR = Get-PullRequest -PRNumber $SourcePRNumber

if ($null -eq $originalPR) {
    throw "PR #$SourcePRNumber not found."
}

Write-Host "Original PR: #$($originalPR.number)"
Write-Host "Source branch: $($originalPR.headRefName)"
Write-Host "Target branch: $($originalPR.baseRefName)"
Write-Host "Title: $($originalPR.title)"

# Generate a unique branch name for the duplicate
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$duplicateBranch = "$($originalPR.headRefName)-duplicate-$timestamp"

Write-Host "Creating duplicate branch: $duplicateBranch"

# Create and checkout the duplicate branch from the source branch
git fetch origin $($originalPR.headRefName)
git checkout -b $duplicateBranch origin/$($originalPR.headRefName)

# Push the duplicate branch
git push -u origin $duplicateBranch

# Create the duplicate PR
$duplicateTitle = "[DUPLICATE] $($originalPR.title)"
$duplicateBody = "This is a duplicate of PR #$SourcePRNumber`n`n---`n`n$($originalPR.body)"

# Extract label names if they exist
$labels = @()
if ($originalPR.labels -and $originalPR.labels.Count -gt 0) {
    $labels = $originalPR.labels | ForEach-Object { $_.name }
}

# Create the PR
if ($labels.Count -gt 0) {
    $labelArgs = $labels | ForEach-Object { "--label", $_ }
    gh pr create --head $duplicateBranch --base $($originalPR.baseRefName) --title $duplicateTitle --body $duplicateBody @labelArgs
} else {
    gh pr create --head $duplicateBranch --base $($originalPR.baseRefName) --title $duplicateTitle --body $duplicateBody
}

# Get the newly created PR details
Start-Sleep -Seconds 2
$newPR = gh api -X GET repos/:owner/:repo/pulls -F head=":owner:$duplicateBranch" -f state=open | ConvertFrom-Json | Select-Object -First 1

if ($newPR) {
    Write-Host "Duplicate PR created successfully!"
    Write-Host "PR Number: $($newPR.number)"
    Write-Host "PR URL: $($newPR.html_url)"
    
    # Set variables for Azure Pipelines
    Write-Host "##vso[task.setvariable variable=DUPLICATE_PR_NUMBER]$($newPR.number)"
    Write-Host "##vso[task.setvariable variable=DUPLICATE_PR_LINK]$($newPR.html_url)"
} else {
    Write-Warning "Duplicate PR may have been created, but unable to retrieve details."
}
