[CmdletBinding()]
param()

$guestAgentPath = $null

try {
    $proc = Get-Process 'WindowsAzureGuestAgent'

    if($proc) {
        if (($proc -is [System.Array]) -and ($proc.Count -gt 0)) {
            $guestAgentPath = $proc[0].Path
        }
        else {
            $guestAgentPath = $proc.Path
        }
    }
}
catch {
}

if($guestAgentPath) {
    Write-Capability -Name 'AzureGuestAgent' -Value $guestAgentPath
}

