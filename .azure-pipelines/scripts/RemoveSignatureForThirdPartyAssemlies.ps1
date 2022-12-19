<#
  .SYNOPSIS
    Script is used as a start point for the process of removing signature from the third party assemlies

  .PARAMETER LayoutRoot
    Parameter that contains path to the _layout directory for current agent build
#>

[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$LayoutRoot
)

. $PSScriptRoot\Get-SigntoolPath.ps1
. $PSScriptRoot\RemoveSignatureScript.ps1

$signtoolPath = Get-Signtool | Select -Last 1

if ( ($signToolPath -ne "") -and (Test-Path -Path $signtoolPath) ) {
  Remove-ThirdPartySignatures -SigntoolPath "$signToolPath" -LayoutRoot "$LayoutRoot"
} else {
  Write-Host "##[error]$signToolPath is not a valid path"
  exit 1
}
