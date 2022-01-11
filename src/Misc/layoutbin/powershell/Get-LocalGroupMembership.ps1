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
    
    # Get a group object using ADSI adpater
    $groupObject = [ADSI]"WinNT://./$Group"
    $groupMemberPaths = @($groupObject.Invoke('Members') | ForEach-Object { ([adsi]$_).path }) 
    Write-Host $groupMemberPaths
    $groupMembers = $groupMemberPaths | ForEach-Object { [regex]::match($_, '^WinNT://(.*)').groups[1].value }

    # Format names as group members are returned with a forward slashes
    $names = $groupMembers.Replace("`/", "`\")

    return ($names -contains $UserName)
}

$user = [Security.Principal.WindowsIdentity]::GetCurrent()
Write-Host "Local group membership for current user: $($user.Name)"
$userGroups = @()

foreach ($group in Get-LocalGroup) {
    # The usernames are returned in the following string format "domain\username"
    try { 
        if (Get-LocalGroupMember -ErrorAction Stop -Group $group | Where-Object name -like $user.Name) {
            $userGroups += $group.name
        }
    } catch {
        try {
            # There is a known issue with Get-LocalGroupMember cmdlet: https://github.com/PowerShell/PowerShell/issues/2996
            # Trying to overcome the issue using ADSI
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
