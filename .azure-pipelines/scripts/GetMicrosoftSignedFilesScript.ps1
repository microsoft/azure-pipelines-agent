[CmdletBinding()]
param(
  [string]$LayoutRoot,
  [string]$ListFilePath
)

function Get-MicrosoftSignedFiles() {
  <#
    .SYNOPSIS
      The script is used to build the list of files that are already signed by Microsoft

    .PARAMETER LayoutRoot
      Parameter that contains path to the _layout directory for current agent build

    .PARAMETER ListFilePath
      Full path (outside the layout) of the text file to write the Microsoft-signed file list to
  #>
  [CmdletBinding()]
  param(
      [Parameter(Mandatory = $true)]
      [string]$LayoutRoot,
      [Parameter(Mandatory = $true)]
      [string]$ListFilePath)

  # A file is Microsoft-signed when its certificate organization is Microsoft Corporation.
  $microsoftSignerPattern = 'O=Microsoft Corporation'

  $outputFile = $ListFilePath
  $layoutFullPath = (Resolve-Path -LiteralPath "$LayoutRoot").Path

  $microsoftSignedFiles = New-Object Collections.Generic.List[String]
  $filesCounter = 0
  foreach ($tree in Get-ChildItem -Path "$LayoutRoot" -Include "*.dll","*.exe" -Recurse | select FullName) {
    $filesCounter = $filesCounter + 1
    try {
      $isMicrosoftSigned = $false
      $authSig = Get-AuthenticodeSignature -LiteralPath "$($tree.FullName)"
      # Only preserve embedded (Authenticode) Microsoft signatures; catalog sigs don't ship with the file.
      if ($authSig.Status -eq 'Valid' -and $null -ne $authSig.SignerCertificate -and $authSig.SignatureType -eq 'Authenticode') {
        if ($authSig.SignerCertificate.Subject -match $microsoftSignerPattern) {
          $isMicrosoftSigned = $true
        }
      }

      if ($isMicrosoftSigned) {
        $relativePath = $tree.FullName.Substring($layoutFullPath.Length).TrimStart('\', '/')
        $microsoftSignedFiles.Add("$relativePath")
        Write-Host "Preserve (embedded Microsoft) - $relativePath"
      } else {
        Write-Host "Skip ($($authSig.SignatureType)/$($authSig.Status)) - $($tree.FullName)"
      }
    } catch {
      $Error.clear()
    }
  }

  $outputDir = Split-Path -Path "$outputFile" -Parent
  if ($outputDir -and -not (Test-Path -LiteralPath "$outputDir")) {
    New-Item -ItemType Directory -Path "$outputDir" -Force | Out-Null
  }

  Set-Content -LiteralPath "$outputFile" -Value $microsoftSignedFiles -Encoding UTF8

  # Publish the list as a pipeline artifact (job-unique name to avoid collisions across OS/arch).
  Write-Host "##vso[artifact.upload artifactname=microsoft-signed-files-$($env:AGENT_JOBNAME)]$outputFile"

  Write-Host "Scanned files - $filesCounter"
  Write-Host "Microsoft-signed files - $($microsoftSignedFiles.Count)"
  Write-Host "List written to - $outputFile"
}

Get-MicrosoftSignedFiles -LayoutRoot "$LayoutRoot" -ListFilePath "$ListFilePath"
