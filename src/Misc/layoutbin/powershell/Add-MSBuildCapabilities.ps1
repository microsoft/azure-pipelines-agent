[CmdletBinding()]
param()

function Get-MSBuildCapabilities {
    param (
        [Parameter(Mandatory = $true)]
        [int]$MajorVersion,

        [switch]$Add_x64
    )

    $vs = Get-VisualStudio -MajorVersion $MajorVersion
    
    $capabilitySuffix = [string]::Empty
    if($Add_x64)
    {
        $msbuildInstallationPath = 'MSBuild\Current\Bin\amd64'
        $capabilitySuffix = "_x64"
    }
    else
    {
        $msbuildInstallationPath = 'MSBuild\Current\Bin'
    }

    if ($vs -and $vs.installationPath) {
        # Add MSBuild_$($MajorVersion).0.
        # End with "\" for consistency with old MSBuildToolsPath value.
        $msbuild = ([System.IO.Path]::Combine($vs.installationPath, $msbuildInstallationPath)) + '\'
        if ((Test-Leaf -LiteralPath "$($msbuild)MSBuild.exe")) {
            Write-Capability -Name "MSBuild_$($MajorVersion).0$($capabilitySuffix)" -Value $msbuild
            $latest = $msbuild
        }
    }
    if ($latest) {
        Write-Capability -Name "MSBuild$($capabilitySuffix)" -Value $latest
    }
}

# Define the key names.
$keyName20 = "Software\Microsoft\MSBuild\ToolsVersions\2.0"
$keyName35 = "Software\Microsoft\MSBuild\ToolsVersions\3.5"
$keyName40 = "Software\Microsoft\MSBuild\ToolsVersions\4.0"
$keyName12 = "Software\Microsoft\MSBuild\ToolsVersions\12.0"
$keyName14 = "Software\Microsoft\MSBuild\ToolsVersions\14.0"

# Add 32-bit.
$latest = $null
$null = Add-CapabilityFromRegistry -Name "MSBuild_2.0" -Hive 'LocalMachine' -View 'Registry32' -KeyName $keyName20 -ValueName 'MSBuildToolsPath' -Value ([ref]$latest)
$null = Add-CapabilityFromRegistry -Name "MSBuild_3.5" -Hive 'LocalMachine' -View 'Registry32' -KeyName $keyName35 -ValueName 'MSBuildToolsPath' -Value ([ref]$latest)
$null = Add-CapabilityFromRegistry -Name "MSBuild_4.0" -Hive 'LocalMachine' -View 'Registry32' -KeyName $keyName40 -ValueName 'MSBuildToolsPath' -Value ([ref]$latest)
$null = Add-CapabilityFromRegistry -Name "MSBuild_12.0" -Hive 'LocalMachine' -View 'Registry32' -KeyName $keyName12 -ValueName 'MSBuildToolsPath' -Value ([ref]$latest)
$null = Add-CapabilityFromRegistry -Name "MSBuild_14.0" -Hive 'LocalMachine' -View 'Registry32' -KeyName $keyName14 -ValueName 'MSBuildToolsPath' -Value ([ref]$latest)
$vs15 = Get-VisualStudio -MajorVersion 15
if ($vs15 -and $vs15.installationPath) {
    # Add MSBuild_15.0.
    # End with "\" for consistency with old MSBuildToolsPath value.
    $msbuild15 = ([System.IO.Path]::Combine($vs15.installationPath, 'MSBuild\15.0\Bin')) + '\'
    if ((Test-Leaf -LiteralPath "$($msbuild15)MSBuild.exe")) {
        Write-Capability -Name 'MSBuild_15.0' -Value $msbuild15
        $latest = $msbuild15
    }
}

Get-MSBuildCapabilities -MajorVersion 16

Get-MSBuildCapabilities -MajorVersion 17

# Add 64-bit.
$latest = $null
$null = Add-CapabilityFromRegistry -Name "MSBuild_2.0_x64" -Hive 'LocalMachine' -View 'Registry64' -KeyName $keyName20 -ValueName 'MSBuildToolsPath' -Value ([ref]$latest)
$null = Add-CapabilityFromRegistry -Name "MSBuild_3.5_x64" -Hive 'LocalMachine' -View 'Registry64' -KeyName $keyName35 -ValueName 'MSBuildToolsPath' -Value ([ref]$latest)
$null = Add-CapabilityFromRegistry -Name "MSBuild_4.0_x64" -Hive 'LocalMachine' -View 'Registry64' -KeyName $keyName40 -ValueName 'MSBuildToolsPath' -Value ([ref]$latest)
$null = Add-CapabilityFromRegistry -Name "MSBuild_12.0_x64" -Hive 'LocalMachine' -View 'Registry64' -KeyName $keyName12 -ValueName 'MSBuildToolsPath' -Value ([ref]$latest)
$null = Add-CapabilityFromRegistry -Name "MSBuild_14.0_x64" -Hive 'LocalMachine' -View 'Registry64' -KeyName $keyName14 -ValueName 'MSBuildToolsPath' -Value ([ref]$latest)
if ($vs15 -and $vs15.installationPath) {
    # Add MSBuild_15.0_x64.
    # End with "\" for consistency with old MSBuildToolsPath value.
    $msbuild15 = ([System.IO.Path]::Combine($vs15.installationPath, 'MSBuild\15.0\Bin\amd64')) + '\'
    if ((Test-Leaf -LiteralPath "$($msbuild15)MSBuild.exe")) {
        Write-Capability -Name 'MSBuild_15.0_x64' -Value $msbuild15
        $latest = $msbuild15
    }
}

Get-MSBuildCapabilities -MajorVersion 16 -Add_x64

Get-MSBuildCapabilities -MajorVersion 17 -Add_x64
