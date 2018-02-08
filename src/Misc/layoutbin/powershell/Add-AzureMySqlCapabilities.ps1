[CmdletBinding()]
param()

function Get-MysqlExePath {
    # First checking in registry
    $hiveViewPairs = @(
        @{ Hive = 'LocalMachine' ; View = 'Registry64' }
        @{ Hive = 'LocalMachine' ; View = 'Registry32' }
    )

    foreach ($pair in $hiveViewPairs) {
        $mysqlKeyPaths = Get-RegistrySubKeyNames -Hive $pair.Hive -View $pair.View  -KeyName 'Software\MySQL AB'  | Where-Object { $_ -like 'MySQL Server*' }
        if($mysqlKeyPaths){
            foreach ($mysqlKeyPath in $mysqlKeyPaths) {
                $location = Get-RegistryValue -Hive $pair.Hive -View $pair.View -KeyName "Software\MySQL AB\$mysqlKeyPath" -ValueName 'Location'
                $installedPath =[System.IO.Path]::Combine($location, 'bin\mysql.exe');
                if ((Test-Leaf -LiteralPath $installedPath)) {
                   return $installedPath;
                }
            }
        }
    }

    # If it is not in registry checking in environment path
    $command = Get-Command -Name 'mysql'
    if($command){
        return $command.Source;
    }
}

$mysqlPath = Get-MysqlExePath
if (!$mysqlPath) {
    return
}

# Output the capability.
Write-Capability -Name 'mysql' -Value $mysqlPath
