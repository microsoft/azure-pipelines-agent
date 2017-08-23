[CmdletBinding()]
param()
$xamarinAndroidVersion = [Version]'0.0.0.0'

# check VS2017 setup packages for latest Xamarin Android version
$vs2017Instances = Get-VSSetupInstance -All | Select-VSSetupInstance -Require 'Xamarin.Android.Sdk'
foreach ($vs2017Instance in $vs2017Instances) {
    $xa = $vs2017Instance.packages | Where { $_.id -match 'Xamarin.Android.Sdk' }
    if ($xa -and $xa.version) {
        $version = [Version]$xa.version
        if ($version -gt $xamarinAndroidVersion) {
            $xamarinAndroidVersion = $version
        }
    }
}
if ($xamarinAndroidVersion -gt [Version]'0.0.0.0') {
        Write-Capability -Name 'Xamarin.Android' -Value $xamarinAndroidVersion
}
else {
    # check legacy registry key for Xamarin Android (VS 2015 and previous versions)
    $null = Add-CapabilityFromRegistry -Name 'Xamarin.Android' -Hive 'LocalMachine' -View 'Registry32' -KeyName 'Software\Novell\Mono for Android' -ValueName 'InstalledVersion'
}