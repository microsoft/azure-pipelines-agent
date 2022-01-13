<#
.SYNOPSIS
    Get a list of local groups the current Windows user belongs to.

.DESCRIPTION
    The Get-LocalGroupMembership.ps1 script gets the current Windows user and prints the local group memberships for that user.
    If Get-LocalGroupMember cmdlet failed to list group members, it tries to check membership using ADSI adapter.
#>
[CmdletBinding()]
param()

function Test-LocalGroupMembershipADSI {
    <#
    .SYNOPSIS
        Checks if a user is a member of a local group using ADSI.
        Returns $true if the user is a member of the group.

    .EXAMPLE
        Test-LocalGroupMembershipADSI -Group "Users" -UserName "Domain/UserName"
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Group,
        [Parameter(Mandatory = $true)]
        [string]$UserName
    )
    
    # Get a group object using ADSI adapter
    $groupObject = [ADSI]"WinNT://./$Group"
    $groupMemberPaths = @($groupObject.Invoke('Members') | ForEach-Object { ([ADSI]$_).path }) 
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
            Write-Warning "Unable to get local group members for group $group"
            Write-Host $_.Exception
        }
    }
}

$userGroups
