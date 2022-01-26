function Remove-ThirdPartySignatures() {
  <#
    .SYNOPSIS
      The script is used to perform signature removal of third party assemblies

    .PARAMETER SigntoolPath
      Path to signtool.exe

    .PARAMETER LayoutRoot
      Parameter that contains path to the _layout directory for current agent build
  #>
  [CmdletBinding()]
  param(
      [Parameter(Mandatory = $true)]
      [string]$SigntoolPath,
      [Parameter(Mandatory = $true)]
      [string]$LayoutRoot)

  $failedToUnsign = New-Object Collections.Generic.List[String]
  $succesfullyUnsigned = New-Object Collections.Generic.List[String]
  $filesWithoutSignatures = New-Object Collections.Generic.List[String]
  $filesCounter = 0
  foreach ($tree in Get-ChildItem -Path "$LayoutRoot" -Include "*.dll","*.exe" -Recurse | select FullName) {
    $filesCounter = $filesCounter + 1
    try {
      # check that file contain a signature before removal
      $verificationOutput = & "$SigntoolPath" verify /pa "$($tree.FullName)" 2>&1 | Write-Output
      $fileDoesntContainSignature = $false;

      if ($verificationOutput -match "No signature found.") {
        $fileDoesntContainSignature = $true;
        $filesWithoutSignatures.Add("$($tree.FullName)")
        $Error.clear()
      }

      if ($fileDoesntContainSignature -ne $true) {
        $removeOutput = & "$SigntoolPath" remove /s "$($tree.FullName)" 2>&1 | Write-Output
        if ($lastExitcode -ne 0) {
          $failedToUnsign.Add("$($tree.FullName)")
          $Error.clear()
        } else {
          $succesfullyUnsigned.Add("$($tree.FullName)")
        }
      }
    } catch {
        $failedToUnsign.Add("$($tree.FullName)")
        $Error.clear()
    }
  }

  Write-host "Failed to unsign - $($failedtounsign.Count)"
  Write-host "Succesfully unsigned - $($succesfullyUnsigned.Count)"
  Write-host "Files without signature - $($filesWithoutSignatures.Count)"
  foreach ($s in $filesWithoutSignatures) {
    Write-Host "File $s doesn't contain signature"
  }
  foreach ($s in $succesfullyunsigned) {
    Write-Host "Signature succefully removed for $s file"
  }

  if ($failedToUnsign.Count -gt 0) {
    foreach ($f in $failedtounsign) {
      Write-Host "##[error]Something went wrong, failed to process $f file"
    }
    exit 1
  }

  exit 0
}
