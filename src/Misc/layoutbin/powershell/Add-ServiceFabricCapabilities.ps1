[CmdletBinding()]
param()

# Define the key names.
$keyNameSfRuntime = "Software\Microsoft\Service Fabric"
$keyNameSfSdk = "Software\Microsoft\Service Fabric SDK"

# Service Fabric Tools are only provided separately for VS v14/2015. They are included the 'Azure Workload' within VS v15/2017.
$rootKeyNameSfTools_14 = "Software\WOW6432Node\Microsoft\Microsoft Azure Service Fabric Visual Studio Tools\14.0"

# Add the capabilities.
$sfRuntime = $null
$sfSdk = $null
$sfTools_14 = $null

Add-CapabilityFromRegistry -Name 'ServiceFabric_Runtime' -Hive 'LocalMachine' -View 'Registry64' -KeyName $keyNameSfRuntime -ValueName 'FabricVersion' -Value ([ref]$sfRuntime)
Add-CapabilityFromRegistry -Name 'ServiceFabric_SDK' -Hive 'LocalMachine' -View 'Registry64' -KeyName $keyNameSfSdk -ValueName 'FabricSDKVersion' -Value ([ref]$sfSdk)

# The Tools Major.minor version will be the name of the SubKey. There can be only one installed at a time.
$sfToolsSubKeys = Get-RegistrySubKeyNames -Hive 'LocalMachine' -View 'Registry64' -KeyName $rootKeyNameSfTools_14
if($sfToolsSubKeys) {
    $sfToolKey = Join-Path "$rootKeyNameSfTools_14" "$sfToolsSubKeys"
    Add-CapabilityFromRegistry -Name 'ServiceFabric_Tools_14' -Hive 'LocalMachine' -View 'Registry64' -KeyName $sfToolKey -ValueName 'FullVersion' -Value ([ref]$sfTools_14)
}

if ($sfRuntime) {
    Write-Capability -Name 'ServiceFabric_Runtime' -Value $sfRuntime
}

if ($sfSdk) {
    Write-Capability -Name 'ServiceFabric_SDK' -Value $sfSdk
}

if ($sfTools_14) {
    Write-Capability -Name 'ServiceFabric_Tools_14' -Value $sfTools_14
}
