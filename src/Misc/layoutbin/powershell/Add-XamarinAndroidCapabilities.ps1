[CmdletBinding()]
param()

if (!(Add-CapabilityFromRegistry -Name 'Xamarin.Android' -Hive 'LocalMachine' -View 'Registry32' -KeyName 'Software\Novell\Mono for Android' -ValueName 'InstalledVersion')) {
    foreach ($vsver in @(17, 16, 15)) {
        $vs = Get-VisualStudio -MajorVersion $vsver
        if ($vs -and $vs.installationPath) {
            # End with "\" for consistency with old ShellFolder values.
            $shellFolder = $vs.installationPath.TrimEnd('\'[0]) + "\"
            $xamarinAndroidDir = ([System.IO.Path]::Combine($shellFolder, 'MSBuild', 'Xamarin', 'Android')) + '\'
            if ((Test-Container -LiteralPath $xamarinAndroidDir)) {
                # Xamarin.Android 7 has a Version file, and this file is renamed to Version.txt in Xamarin.Android 8.x
                $found = $false
                foreach ($file in @('Version', 'Version.txt')) {
                    $versionFile = ([System.IO.Path]::Combine($xamarinAndroidDir, $file))
                    $version = Get-Content -ErrorAction ignore -TotalCount 1 -LiteralPath $versionFile
                    if ($version) {
                        Write-Capability -Name 'Xamarin.Android' -Value $version.trim()
                        $found = $true
                        break
                    }
                }
                if ($found) {
                    break
                }
            }
        }
    }
}
