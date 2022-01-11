[CmdletBinding()]
param()

# Checks if a user is a member of a group using ADSI
# Returns $true if the user is a member of the group
function Test-LocalGroupMembershipADSI {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Group,
        [Parameter(Mandatory = $true)]
        [string]$UserName
    )
    $g = [ADSI]"WinNT://$env:COMPUTERNAME/$Group"
    $groupMembers = @($g.Invoke('Members') | ForEach-Object { ([adsi]$_).path }) 
    $names = foreach ($member in $groupMembers) {
        $x = [regex]::match($member, '^WinNT://(.*)').groups[1].value;
        $x.Replace("`/", "`\");
    }
    return ($names -contains $UserName)
}

$user = [Security.Principal.WindowsIdentity]::GetCurrent()
Write-Host "Local group membership for current user: $($user.Name)"
$userGroups = @()

foreach ($group in Get-LocalGroup) {
    # the usernames are returned in the string form "computername\username"
    try { 
        if (Get-LocalGroupMember -ErrorAction Stop -Group $group | Where-Object name -like $user.Name) {
            $userGroups += $group.name
        }
    } catch {
        try {
            # there is a known issue with Get-LocalGroupMember cmdlet: https://github.com/PowerShell/PowerShell/issues/2996
            # trying to overcome the issue using ADSI
            if (Test-LocalGroupMembershipADSI -Group $group -UserName $user.Name) {
                $userGroups += $group.name
            }
        } catch {
            Write-Warning "Unable to get local group memebers for group $group"
            Write-Host $_.Exception
        }
    }
}

$userGroups
