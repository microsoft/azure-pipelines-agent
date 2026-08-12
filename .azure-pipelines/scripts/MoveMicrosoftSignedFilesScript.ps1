<#
  .SYNOPSIS
    Moves Microsoft-signed files out of the layout before 3rd-party signing and back afterwards,
    so the 3rd-party signing step cannot re-sign them.

  .PARAMETER LayoutRoot
    Path to the _layout directory for current agent build

  .PARAMETER ListFilePath
    Full path of the Microsoft-signed list file produced by the detection step

  .PARAMETER HoldingRoot
    Folder (outside the layout) used to hold the files while 3rd-party signing runs

  .PARAMETER Mode
    Stash = move files out of the layout, Restore = move them back
#>

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$LayoutRoot,
  [Parameter(Mandatory = $true)]
  [string]$ListFilePath,
  [Parameter(Mandatory = $true)]
  [string]$HoldingRoot,
  [Parameter(Mandatory = $true)]
  [ValidateSet('Stash', 'Restore')]
  [string]$Mode
)

$listFile = $ListFilePath
if (-not (Test-Path -LiteralPath "$listFile")) {
  Write-Host "Microsoft-signed list not found - nothing to $Mode"
  return
}

$movedCounter = 0
foreach ($line in Get-Content -LiteralPath "$listFile") {
  $relativePath = $line.Trim()
  if ($relativePath -eq "") {
    continue
  }

  if ($Mode -eq 'Stash') {
    $source = Join-Path "$LayoutRoot" $relativePath
    $destination = Join-Path "$HoldingRoot" $relativePath
  } else {
    $source = Join-Path "$HoldingRoot" $relativePath
    $destination = Join-Path "$LayoutRoot" $relativePath
  }

  if (-not (Test-Path -LiteralPath "$source")) {
    continue
  }

  $destinationDir = Split-Path -Path "$destination" -Parent
  if (-not (Test-Path -LiteralPath "$destinationDir")) {
    New-Item -ItemType Directory -Path "$destinationDir" -Force | Out-Null
  }

  Move-Item -LiteralPath "$source" -Destination "$destination" -Force
  $movedCounter = $movedCounter + 1
}

Write-Host "$Mode complete - $movedCounter Microsoft-signed file(s) moved"
