#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Scans Azure DevOps pipelines for usage of deprecated tasks.

.DESCRIPTION
    Fetches the list of deprecated tasks from the azure-pipelines-tasks
    DEPRECATION.md on GitHub, then queries all pipeline definitions in the
    specified Azure DevOps project and reports which pipelines reference
    deprecated tasks.

.PARAMETER accountUrl
    The Azure DevOps organization URL (e.g. https://dev.azure.com/myorg).

.PARAMETER pat
    A Personal Access Token with permissions to read pipeline definitions.

.PARAMETER project
    The Azure DevOps project name to scan.

.PARAMETER outputCsv
    Optional path to export results as CSV.

.EXAMPLE
    .\QueryPipelinesForDeprecatedTasks.ps1 -accountUrl https://dev.azure.com/myorg -pat $myPat -project MyProject
#>

param (
    [Parameter(Mandatory = $true)]
    [string] $accountUrl,

    [Parameter(Mandatory = $true)]
    [string] $pat,

    [Parameter(Mandatory = $true)]
    [string] $project,

    [Parameter(Mandatory = $false)]
    [string] $outputCsv
)

$ErrorActionPreference = 'Stop'

# --- Auth header ---
$base64authinfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$pat"))
$headers = @{
    "Authorization" = "Basic $base64authinfo"
    "Content-Type"  = "application/json"
    "Accept"        = "application/json"
}

# --- Step 1: Fetch deprecated tasks from DEPRECATION.md ---
Write-Host "Fetching deprecated tasks list from GitHub..." -ForegroundColor Cyan
$deprecationUrl = "https://raw.githubusercontent.com/microsoft/azure-pipelines-tasks/master/DEPRECATION.md"
$deprecationContent = Invoke-RestMethod -Uri $deprecationUrl -Method GET

# Parse the markdown table: rows look like "| TaskName VN | PR | Date |"
$deprecatedTasks = @{}
foreach ($line in $deprecationContent -split "`n") {
    $line = $line.Trim()
    if ($line -match '^\|\s*(\w+)\s+V(\d+)\s*\|') {
        $taskName = $Matches[1]
        $taskVersion = $Matches[2]
        $key = "$taskName@$taskVersion"
        if (-not $deprecatedTasks.ContainsKey($key)) {
            $deprecatedTasks[$key] = @{ Name = $taskName; Version = $taskVersion }
        }
    }
}

Write-Host ("Found " + $deprecatedTasks.Count + " deprecated task versions in DEPRECATION.md`n") -ForegroundColor Green

# Also build a regex pattern to match "- task: TaskName@N" in YAML
# and "taskDefinition.name == TaskName" style references
$taskPatterns = $deprecatedTasks.Keys | ForEach-Object {
    $parts = $_ -split '@'
    [regex]::Escape($parts[0]) + '@' + [regex]::Escape($parts[1])
}
$combinedPattern = ($taskPatterns -join '|')

# --- Step 2: Get all pipeline definitions ---
Write-Host "Querying pipeline definitions from $project..." -ForegroundColor Cyan

$allPipelines = @()
$continuationToken = $null

do {
    $url = "$accountUrl/$project/_apis/pipelines?api-version=7.0&`$top=100"
    if ($continuationToken) {
        $url += "&continuationToken=$continuationToken"
    }

    $response = Invoke-WebRequest -Uri $url -Headers $headers -Method GET
    if ($response.StatusCode -ne 200) {
        throw "Failed to query pipelines: $($response.Content)"
    }

    $continuationToken = $response.Headers.'x-ms-continuationtoken'
    $json = ConvertFrom-Json $response.Content
    $allPipelines += $json.value

    Write-Host ("  Fetched " + $json.value.Count + " pipelines (total: " + $allPipelines.Count + ")")
} while ($continuationToken)

Write-Host ("Total pipelines found: " + $allPipelines.Count + "`n") -ForegroundColor Green

# --- Step 3: Check each pipeline's YAML for deprecated tasks ---
Write-Host "Scanning pipelines for deprecated task usage..." -ForegroundColor Cyan

$results = @()
$scanned = 0
$errorCount = 0

foreach ($pipeline in $allPipelines) {
    $scanned++
    if ($scanned % 50 -eq 0) {
        Write-Host ("  Scanned $scanned / " + $allPipelines.Count + " pipelines...")
    }

    try {
        # Get the pipeline YAML content
        $yamlUrl = "$accountUrl/$project/_apis/pipelines/$($pipeline.id)?api-version=7.0&`$expand=yaml"
        $yamlResponse = Invoke-WebRequest -Uri $yamlUrl -Headers $headers -Method GET

        if ($yamlResponse.StatusCode -ne 200) {
            continue
        }

        $pipelineDetail = ConvertFrom-Json $yamlResponse.Content
        $yamlContent = $null

        # The YAML may be in the configuration property
        if ($pipelineDetail.configuration -and $pipelineDetail.configuration.type -eq 'yaml') {
            # For YAML pipelines, fetch the YAML file content
            $repoId = $pipelineDetail.configuration.repository.id
            $yamlPath = $pipelineDetail.configuration.path
            $branch = $pipelineDetail.configuration.repository.defaultBranch

            if ($yamlPath -and $repoId) {
                $fileUrl = "$accountUrl/$project/_apis/git/repositories/$repoId/items?path=$([Uri]::EscapeDataString($yamlPath))&api-version=7.0"
                if ($branch) {
                    $fileUrl += "&versionDescriptor.version=$([Uri]::EscapeDataString($branch -replace '^refs/heads/',''))&versionDescriptor.versionType=branch"
                }
                try {
                    $fileResponse = Invoke-WebRequest -Uri $fileUrl -Headers $headers -Method GET
                    $yamlContent = $fileResponse.Content
                } catch {
                    # File may not be accessible
                }
            }
        }

        if (-not $yamlContent) {
            continue
        }

        # Find all deprecated task references in the YAML
        $matches = [regex]::Matches($yamlContent, $combinedPattern)
        if ($matches.Count -gt 0) {
            $foundTasks = ($matches | ForEach-Object { $_.Value } | Sort-Object -Unique)
            foreach ($taskRef in $foundTasks) {
                $results += [PSCustomObject]@{
                    PipelineId   = $pipeline.id
                    PipelineName = $pipeline.name
                    PipelineUrl  = "$accountUrl/$project/_build?definitionId=$($pipeline.id)"
                    DeprecatedTask = $taskRef
                    YamlPath     = $yamlPath
                }
            }
        }
    }
    catch {
        $errorCount++
    }
}

# --- Step 4: Report results ---
Write-Host ("`nScan complete. Scanned $scanned pipelines ($errorCount errors).`n") -ForegroundColor Cyan

if ($results.Count -eq 0) {
    Write-Host "No deprecated task usage found." -ForegroundColor Green
}
else {
    Write-Host ("Found " + $results.Count + " deprecated task references across pipelines:`n") -ForegroundColor Yellow

    $grouped = $results | Group-Object -Property DeprecatedTask | Sort-Object -Property Count -Descending
    Write-Host "=== Summary by Deprecated Task ===" -ForegroundColor Cyan
    foreach ($group in $grouped) {
        Write-Host ("  $($group.Name): $($group.Count) pipeline(s)") -ForegroundColor Yellow
    }

    Write-Host ("`n=== Detailed Results ===") -ForegroundColor Cyan
    $results | Format-Table -Property PipelineName, DeprecatedTask, PipelineUrl -AutoSize

    if ($outputCsv) {
        $results | Export-Csv -Path $outputCsv -NoTypeInformation -Encoding UTF8
        Write-Host ("Results exported to: $outputCsv") -ForegroundColor Green
    }
}

# Output the results object for programmatic use
$results
