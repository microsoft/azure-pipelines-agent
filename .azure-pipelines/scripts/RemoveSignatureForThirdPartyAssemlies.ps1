<#
  .SYNOPSIS
    Script is used as a start point for the process of removing signature from the third party assemlies

  .PARAMETER LayoutRoot
    Parameter that contains path to the _layout directory for current agent build

  .PARAMETER ListFilePath
    Full path of the Microsoft-signed list file produced by the detection step
#>

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$LayoutRoot,
  [Parameter(Mandatory = $true)]
  [string]$ListFilePath
)

. $PSScriptRoot\Get-SigntoolPath.ps1
. $PSScriptRoot\RemoveSignatureScript.ps1

# Read the Microsoft-signed list produced by the detection step.
$microsoftSignedListFile = $ListFilePath

$signtoolPath = Get-Signtool | Select -Last 1

if ( ($signToolPath -ne "") -and (Test-Path -Path $signtoolPath) ) {
  Remove-ThirdPartySignatures -SigntoolPath "$signToolPath" -LayoutRoot "$LayoutRoot" -MicrosoftSignedListFile "$microsoftSignedListFile"
} else {
  Write-Host "##[error]$signToolPath is not a valid path"
  exit 1
}
