[CmdletBinding()]
param()
function Test-LocalGroupMembershipADSI {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Group,
        [Parameter(Mandatory = $true)]
        [string]$UserName
    )
    $g = [ADSI]"WinNT://$env:COMPUTERNAME/$Group"
    $group_members = @($g.Invoke('Members') | ForEach-Object { ([adsi]$_).path }) 
    $names = foreach ($member in $group_members) {
        $x = [regex]::match($member, '^WinNT://(.*)').groups[1].value;
        $x.Replace("`/", "`\");
    }
    if ($names -contains $UserName) {
        $true
    } else {
        $false
    }
}

$user = [Security.Principal.WindowsIdentity]::GetCurrent()
Write-Host "Local group membership for current user: $($user.Name)"
$userGroups = @()

foreach ($group in Get-LocalGroup) {
    # the usernames are returned in the string form "computername\username"
    try { 
        if (Get-LocalGroupMember -ErrorAction Stop  -Group $group | Where-Object name -like $user.Name) {
            $userGroups += $group.name
        }
    } catch {
        try {
            #known issue: https://github.com/PowerShell/PowerShell/issues/2996
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
