# Test Helper Script for EOL Node.js Feature Testing
# This script provides utility functions to test the node handler logic
# Place this in your test directory and reference from the pipeline

param(
    [Parameter(Mandatory=$true)]
    [string]$TestScenario,
    
    [Parameter(Mandatory=$false)]
    [string]$HandlerDataType = "Node20",
    
    [Parameter(Mandatory=$false)]
    [hashtable]$EnvironmentVars = @{},
    
    [Parameter(Mandatory=$false)]
    [string]$ExpectedNodePath,
    
    [Parameter(Mandatory=$false)]
    [bool]$ExpectedSuccess = $true,
    
    [Parameter(Mandatory=$false)]
    [bool]$DryRun = $true
)

Write-Host "=== EOL Node.js Feature Test Helper ===" -ForegroundColor Cyan
Write-Host "Test Scenario: $TestScenario" -ForegroundColor Yellow
Write-Host "Handler Data Type: $HandlerDataType" -ForegroundColor Yellow
Write-Host "Expected Success: $ExpectedSuccess" -ForegroundColor Yellow
if ($ExpectedNodePath) {
    Write-Host "Expected Node Path: $ExpectedNodePath" -ForegroundColor Yellow
}

# Set environment variables for this test
Write-Host "`n--- Setting Environment Variables ---" -ForegroundColor Green
foreach ($kvp in $EnvironmentVars.GetEnumerator()) {
    $name = $kvp.Key
    $value = $kvp.Value
    [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    Write-Host "Set $name = $value" -ForegroundColor Gray
}

# Common test environment variables
Write-Host "`n--- Current Agent Environment ---" -ForegroundColor Green
Write-Host "Agent OS: $env:AGENT_OS"
Write-Host "Agent Architecture: $env:AGENT_OSARCHITECTURE" 
Write-Host "Agent Version: $env:AGENT_VERSION"
Write-Host "Agent Home Directory: $env:AGENT_HOMEDIRECTORY"

# Display current Node-related environment variables
Write-Host "`n--- Node-Related Environment Variables ---" -ForegroundColor Green
$nodeEnvVars = @(
    "AGENT_USE_NODE24",
    "AGENT_USE_NODE20_1", 
    "AGENT_USE_NODE10",
    "AGENT_USE_NODE24_WITH_HANDLER_DATA",
    "AGENT_ENABLE_EOL_NODE_VERSION_POLICY",
    "AGENT_USE_NODE_PATH",
    "AGENT_USE_NODE20_IN_UNSUPPORTED_SYSTEM",
    "AGENT_USE_NODE24_IN_UNSUPPORTED_SYSTEM",
    "AGENT_DISABLE_NODE6_TASKS",
    "VSTSAGENT_ENABLE_NODE_WARNINGS"
)

foreach ($envVar in $nodeEnvVars) {
    $value = [Environment]::GetEnvironmentVariable($envVar)
    if ($value) {
        Write-Host "$envVar = $value" -ForegroundColor White
    } else {
        Write-Host "$envVar = <not set>" -ForegroundColor DarkGray
    }
}

# Check available Node.js installations on the agent
Write-Host "`n--- Available Node.js Installations ---" -ForegroundColor Green
$agentExternalsDir = Join-Path $env:AGENT_HOMEDIRECTORY "externals"
if (Test-Path $agentExternalsDir) {
    $nodeDirs = Get-ChildItem -Path $agentExternalsDir -Directory | Where-Object { $_.Name -like "node*" }
    foreach ($nodeDir in $nodeDirs) {
        $nodePath = Join-Path $nodeDir.FullName "bin\node.exe"
        if ($IsLinux -or $IsMacOS) {
            $nodePath = Join-Path $nodeDir.FullName "bin/node"
        }
        
        if (Test-Path $nodePath) {
            try {
                $version = & $nodePath --version 2>$null
                Write-Host "$($nodeDir.Name): $version (Path: $nodePath)" -ForegroundColor White
            } catch {
                Write-Host "$($nodeDir.Name): <version check failed> (Path: $nodePath)" -ForegroundColor Yellow
            }
        } else {
            Write-Host "$($nodeDir.Name): <no executable found>" -ForegroundColor Red
        }
    }
} else {
    Write-Host "Agent externals directory not found: $agentExternalsDir" -ForegroundColor Red
}

if ($DryRun) {
    Write-Host "`n--- DRY RUN MODE ---" -ForegroundColor Magenta
    Write-Host "This is a dry run. To execute actual tests, set -DryRun to false"
    Write-Host "Test scenario '$TestScenario' would be executed with the above configuration."
    return
}

# TODO: Add actual test execution logic here
Write-Host "`n--- Executing Test ---" -ForegroundColor Green

# This is where you would integrate with your actual NodeHandler test logic
# For example:
# 1. Create a mock task definition with the specified handler data type
# 2. Trigger the node selection logic in NodeHandler.GetNodeLocation()
# 3. Capture the selected node path
# 4. Compare with expected result

Write-Host "Test execution would happen here..." -ForegroundColor Yellow
Write-Host "Scenario: $TestScenario" 
Write-Host "Handler Type: $HandlerDataType"
Write-Host "Environment configured for this test scenario."

# Mock test result for now
$testPassed = $true
$actualNodePath = "mock/node/path"

Write-Host "`n--- Test Results ---" -ForegroundColor Green
if ($testPassed -eq $ExpectedSuccess) {
    Write-Host "✅ TEST PASSED" -ForegroundColor Green
    Write-Host "Scenario: $TestScenario"
    Write-Host "Result matched expectation: $ExpectedSuccess"
    if ($ExpectedNodePath -and $actualNodePath) {
        if ($actualNodePath -eq $ExpectedNodePath) {
            Write-Host "✅ Node path matched: $actualNodePath" -ForegroundColor Green
        } else {
            Write-Host "❌ Node path mismatch!" -ForegroundColor Red
            Write-Host "  Expected: $ExpectedNodePath" -ForegroundColor Yellow
            Write-Host "  Actual:   $actualNodePath" -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "❌ TEST FAILED" -ForegroundColor Red
    Write-Host "Scenario: $TestScenario"
    Write-Host "Expected success: $ExpectedSuccess, Actual: $testPassed"
}

# Clean up environment variables after test
Write-Host "`n--- Cleaning Up Environment Variables ---" -ForegroundColor Gray
foreach ($envVar in $nodeEnvVars) {
    [Environment]::SetEnvironmentVariable($envVar, $null, 'Process')
}

Write-Host "`nTest helper script completed." -ForegroundColor Cyan