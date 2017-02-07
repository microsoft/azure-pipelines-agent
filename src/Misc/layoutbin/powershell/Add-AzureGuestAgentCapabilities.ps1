[CmdletBinding()]
param()

try {
    $proc = Get-Process WindowsAzureGuestAgent
    if ($proc) {
        Write-Capability -Name 'AzureGuestAgent' -Value 'Yes'
    }
}
catch {
}

