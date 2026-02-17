#!/usr/bin/env pwsh
<# 
.SYNOPSIS 
    Predict whether agents will be able to upgrade from pipeline agent v3 or v4 to agent v5

.DESCRIPTION 
    The Azure Pipeline agent v3 uses .NET 6, and agent v4 uses .NET 8, while agent v5 runs on .NET 10. This means agent v5 will drop support for operating systems not supported by .NET 10 (https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)
    This script will try to predict whether an agent will be able to upgrade, using the osDescription attribute of the agent. For Linux and macOS, this contains the output of 'uname -a`.
    Note the Pipeline agent has more context about the operating system of the host it is running on (e.g. 'lsb_release -a' output), and is able to make a better informed decision on whether to upgrade or not.
    Hence the output of this script is an indication wrt what the agent will do, but will include results where there is no sufficient information to include a prediction.

    This script requires a PAT token with read access on 'Agent Pools' scope.

    For more information, go to https://aka.ms/azdo-pipeline-agent-version.

.EXAMPLE
    ./QueryAgentPoolsForCompatibleOS.ps1 -Token "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
.EXAMPLE
    $env:AZURE_DEVOPS_EXT_PAT = "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
    ./QueryAgentPoolsForCompatibleOS.ps1 -PoolId 1234 -Filter V5InCompatible -Verbose -OpenCsv
#> 

#Requires -Version 7.2

[CmdletBinding(DefaultParameterSetName="pool")]
param ( 
    [parameter(Mandatory=$false,ParameterSetName="pool")]
    [string]
    $OrganizationUrl=$env:AZDO_ORG_SERVICE_URL,
    
    [parameter(Mandatory=$false,ParameterSetName="pool")]
    [int[]]
    $PoolId,
    
    [parameter(Mandatory=$false,ParameterSetName="pool")]
    [int]
    $MaxPools=4096,
    
    [parameter(Mandatory=$false,HelpMessage="PAT token with read access on 'Agent Pools' scope",ParameterSetName="pool")]
    [string]
    $Token=($env:AZURE_DEVOPS_EXT_PAT ?? $env:AZDO_PERSONAL_ACCESS_TOKEN),
    
    [parameter(Mandatory=$false,ParameterSetName="os")]
    [string[]]
    $OS,

    [parameter(Mandatory=$false)]
    [parameter(ParameterSetName="pool")]
    [parameter(ParameterSetName="os")]
    [ValidateSet("All", "ExcludeMissingOS", "MissingOS", "V5Compatible", "V5CompatibilityIssues", "V5CompatibilityUnknown", "V5InCompatible")]
    [string]
    $Filter="V5CompatibilityIssues",

    [parameter(Mandatory=$false)]
    [switch]
    $OpenCsv=$false,

    [parameter(Mandatory=$false)]
    [switch]
    $IncludeMissingOSInStatistics=$false,

    [parameter(Mandatory=$false,HelpMessage="Do not ask for input to start processing",ParameterSetName="pool")]
    [switch]
    $Force=$false
) 

class ClassificationResult {
    hidden [int]$_sortOrder = 1
    hidden [string]$_upgradeStatement = "OS (version) unknown, agent won't upgrade to v5 automatically"
    [ValidateSet($null, $true, $false)]
    hidden [object]$_v5AgentSupportsOS
    [ValidateSet("MissingOS", "Unsupported", "Unknown", "UnknownOS", "UnknownOSVersion", "UnsupportedOSVersion", "Supported")]
    hidden [string]$_v5AgentSupportsOSText = "Unknown"
    [string]$_reason

    ClassificationResult() {
        $this | Add-Member -Name Reason -MemberType ScriptProperty -Value {
            # Get
            return $this._reason
        } -SecondValue {
            # Set
            param($value)
            $this._reason = $value
            Write-Debug "ClassificationResult.Reason = ${value}"
        }
        $this | Add-Member -Name SortOrder -MemberType ScriptProperty -Value {
            return $this._sortOrder 
        }
        $this | Add-Member -Name UpgradeStatement -MemberType ScriptProperty -Value {
            # Get
            return $this._upgradeStatement
        } -SecondValue {
            # Set
            param($value)

            $this._upgradeStatement = $value
        }
        $this | Add-Member -Name V5AgentSupportsOS -MemberType ScriptProperty -Value {
            # Get
            return $this._v5AgentSupportsOS
        } -SecondValue {
            # Set
            param($value)

            $this._v5AgentSupportsOS = $value
            if ($null -eq $value) {
                $this._sortOrder = 1
                $this._v5AgentSupportsOSText = "Unknown"
                $this._upgradeStatement = "OS (version) unknown, agent won't upgrade to v5 automatically"
            } elseif ($value) {
                $this._sortOrder = 2
                $this._v5AgentSupportsOSText = "Supported"
                $this._upgradeStatement = "OS supported by v5 agent, agent will automatically upgrade to v5"
            } else {
                $this._sortOrder = 0
                $this._v5AgentSupportsOSText = "Unsupported"
                $this._upgradeStatement = "OS not supported by v5 agent, agent won't upgrade to v5"
            }
        }
        $this | Add-Member -Name V5AgentSupportsOSText -MemberType ScriptProperty -Value {
            # Get
            return $this._v5AgentSupportsOSText 
        } -SecondValue {
            # Set
            param($value)

            $this._v5AgentSupportsOSText = $value
        }
    }
}

function Classify-OS (
    [parameter(Mandatory=$false)][string]$AgentOS,
    [parameter(Mandatory=$true)][psobject]$Agent
) {
    Write-Debug "AgentOS: ${AgentOS}"
    $result = Validate-OS -OSDescription $AgentOS
    $Agent | Add-Member -NotePropertyName ValidationResult -NotePropertyValue $result
}

function Filter-Agents (
    [parameter(Mandatory=$true,ValueFromPipeline=$true)][psobject[]]$Agents,
    [parameter(Mandatory=$true)][string]$AgentFilter
) {
    process {
        switch ($AgentFilter) {
            "All" {
                $Agents
            }
            "ExcludeMissingOS" {
                $Agents | Where-Object {![string]::IsNullOrWhiteSpace($_.OS)}
            } 
            "MissingOS" {
                $Agents | Where-Object {[string]::IsNullOrWhiteSpace($_.OS)}
            } 
            "V5Compatible" {
                $Agents | Where-Object {$_.ValidationResult.V5AgentSupportsOS -eq $true}
            } 
            "V5CompatibilityIssues" {
                $Agents | Where-Object {$_.ValidationResult.V5AgentSupportsOS -ne $true} | Where-Object {![string]::IsNullOrWhiteSpace($_.OS)}
            } 
            "V5CompatibilityUnknown" {
                $Agents | Where-Object {$null -eq $_.ValidationResult.V5AgentSupportsOS} 
            } 
            "V5InCompatible" {
                $Agents | Where-Object {$_.ValidationResult.V5AgentSupportsOS -eq $false}
            } 
            default {
                $Agents
            }
        }    
    }
}

function Open-Document (
    [parameter(Mandatory=$true)][string]$Document
) {
    if ($IsMacOS) {
        open $Document
        return
    }
    if ($IsWindows) {
        Start-Process $Document
        return
    }
}

function Validate-OS {
    [OutputType([ClassificationResult])]
    param (
        [parameter(Mandatory=$false)][string]$OSDescription
    )

    $result = [ClassificationResult]::new()

    if (!$OSDescription) {
        $result = [ClassificationResult]::new()
        $result.UpgradeStatement = "OS description missing"
        $result.V5AgentSupportsOSText = "MissingOS"
        return $result
    }

    # Parse operating system description
    switch -regex ($OSDescription) {
        # Debian 12 (Bookworm) "Linux 6.1.0-18-amd64 #1 SMP PREEMPT_DYNAMIC Debian 6.1.76-1 (2024-02-01)"
        "(?im)^Linux.* Debian (?<Major>[\d]+)(\.(?<Minor>[\d]+))(\.(?<Build>[\d]+))?.*$" {
            Write-Debug "Debian: '$OSDescription'"
            # The regex captures the Debian token version from uname output (e.g. Debian 6.1.76-1 -> 6.1).
            [version]$kernelVersion = ("{0}.{1}" -f $Matches["Major"],$Matches["Minor"])
            Write-Debug "Debian Linux Kernel $($kernelVersion.ToString())"
            [version]$minKernelVersion = '6.1' # Debian 12 (Bookworm) baseline: https://wiki.debian.org/DebianBookworm

            if ($kernelVersion -ge $minKernelVersion) {
                $result.Reason = "Supported Debian Linux kernel version: ${kernelVersion}"
                $result.V5AgentSupportsOS = $true
                return $result
            } else {
                $result.Reason = "Unsupported Debian Linux kernel version: ${kernelVersion} (see https://wiki.debian.org/DebianReleases)"
                $result.V5AgentSupportsOS = $false
                $result.V5AgentSupportsOSText = "UnsupportedOSVersion"
                return $result
            }
        }
        # Fedora 42 "Linux 6.8.5-300.fc42.x86_64 #1 SMP PREEMPT_DYNAMIC Fedora 42"
        "(?im)^Linux.*\.fc(?<Major>[\d]+)\..*$" {
            Write-Debug "Fedora: '$OSDescription'"
            # Parse Fedora release from the .fcNN token and require Fedora 42+.
            [int]$fedoraVersion = $Matches["Major"]
            Write-Debug "Fedora ${fedoraVersion}"

            if ($fedoraVersion -ge 42) {
                $result.Reason = "Supported Fedora version: ${fedoraVersion}"
                $result.V5AgentSupportsOS = $true
                return $result
            } else {
                $result.Reason = "Unsupported Fedora version: ${fedoraVersion}"
                $result.V5AgentSupportsOS = $false
                $result.V5AgentSupportsOSText = "UnsupportedOSVersion"
                return $result
            }
        }
        # Red Hat / CentOS "Linux 4.18.0-425.3.1.el8.x86_64 #1 SMP Fri Sep 30 11:45:06 EDT 2022"
        "(?im)^Linux.*\.el(?<Major>[\d]+).*$" {
            Write-Debug "Red Hat / CentOS / Oracle Linux: '$OSDescription'"
            [int]$majorVersion = $Matches["Major"]
            Write-Debug "Red Hat ${majorVersion}"

            if ($majorVersion -ge 8) {
                $result.Reason = "Supported RHEL / CentOS / Oracle Linux version: ${majorVersion}"
                $result.V5AgentSupportsOS = $true
                return $result
            } else {
                $result.Reason = "Unsupported RHEL / CentOS / Oracle Linux version: ${majorVersion}"
                $result.V5AgentSupportsOS = $false
                $result.V5AgentSupportsOSText = "UnsupportedOSVersion"
                return $result
            }
        }
        # Ubuntu "Linux 6.8.0-1018-azure #18-Ubuntu SMP Fri Jun 14 12:34:56 UTC 2024" (e.g. 24.04)
        "(?im)^Linux.*[^\d]+((?<Major>[\d]+)((\.(?<Minor>[\d]+))(\.(?<Build>[\d]+)))(\.(?<Revision>[\d]+))?)-Ubuntu.*$" {
            Write-Debug "Ubuntu: '$OSDescription'"
            # Preferred Ubuntu path: parse distro version and allow-list supported releases only.
            [int]$majorVersion = $Matches["Major"]
            [int]$minorVersion = $Matches["Minor"]
            [string]$ubuntuVersion = ("{0}.{1:D2}" -f $majorVersion, $minorVersion)
            [string[]]$supportedUbuntuVersions = @('22.04', '24.04', '25.10')
            Write-Debug "Ubuntu ${majorVersion}"
            Write-Debug "Ubuntu version ${ubuntuVersion}"

            if ($ubuntuVersion -in $supportedUbuntuVersions) {
                $result.Reason = "Supported Ubuntu version: ${ubuntuVersion}"
                $result.V5AgentSupportsOS = $true
                return $result
            }

            $result.Reason = "Unsupported Ubuntu version: ${ubuntuVersion}"
            $result.V5AgentSupportsOS = $false
            $result.V5AgentSupportsOSText = "UnsupportedOSVersion"
            return $result
        }
        # Ubuntu "Linux 5.15.0-1023-azure #29-Ubuntu SMP Wed Oct 19 22:37:08 UTC 2022 x86_64 x86_64 x86_64 GNU/Linux" (22.04)
        # Ubuntu "Linux 6.8.0-1018-azure #18-Ubuntu SMP Fri Jun 14 12:34:56 UTC 2024 x86_64 x86_64 x86_64 GNU/Linux" (24.04)
        "(?im)^Linux (?<KernelMajor>[\d]+)(\.(?<KernelMinor>[\d]+)).*-Ubuntu.*$" {
            Write-Debug "Ubuntu (no version declared): '$OSDescription'"
            # Fallback Ubuntu path when distro version is missing in osDescription.
            # Uses kernel baseline heuristic (5.15+) aligned to supported set starting at Ubuntu 22.04.
            [version]$kernelVersion = ("{0}.{1}" -f $Matches["KernelMajor"],$Matches["KernelMinor"])
            Write-Debug "Ubuntu Linux Kernel $($kernelVersion.ToString())"
            [version]$minKernelVersion = '5.15' # 22.04 baseline for supported set (22.04, 24.04, 25.10)

            if ($kernelVersion -lt $minKernelVersion ) {
                $result.Reason = "Unsupported Ubuntu Linux kernel version: ${kernelVersion}` (see https://ubuntu.com/kernel/lifecycle)"
                $result.V5AgentSupportsOS = $false
                $result.V5AgentSupportsOSText = "UnsupportedOSVersion"
                return $result
            }

            $result.Reason = "Supported Ubuntu Linux kernel version: ${kernelVersion} (>= ${minKernelVersion} baseline)"
            $result.V5AgentSupportsOS = $true
            return $result
        }
        # macOS 14+ "Darwin 23.6.0 Darwin Kernel Version 23.6.0: root:xnu-10063.141.1~1/RELEASE_ARM64_T6031"
        "(?im)^Darwin (?<DarwinMajor>[\d]+)(\.(?<DarwinMinor>[\d]+)).*$" {
            Write-Debug "macOS (Darwin): '$OSDescription'"
            # Darwin major 23 corresponds to macOS 14 (Sonoma); require 23.0+.
            [version]$darwinVersion = ("{0}.{1}" -f $Matches["DarwinMajor"],$Matches["DarwinMinor"])
            Write-Debug "Darwin $($darwinVersion.ToString())"
            [version]$minDarwinVersion = '23.0' # macOS 14 (Sonoma) and newer

            if ($darwinVersion -ge $minDarwinVersion) {
                $result.Reason = "Supported Darwin (macOS) version: ${darwinVersion}"
                $result.V5AgentSupportsOS = $true
                return $result
            } else {
                $result.Reason = "Unsupported Darwin (macOS) version): ${darwinVersion} (see https://en.wikipedia.org/wiki/Darwin_(operating_system)"
                $result.V5AgentSupportsOS = $false
                $result.V5AgentSupportsOSText = "UnsupportedOSVersion"
                return $result
            }
        }
        # Windows support policy:
        # - Windows Client: 10 (1607+) and 11 (22631+)
        # - Windows Server / Server Core / Nano Server: 2016+
        # Example: "Microsoft Windows 10.0.20348"
        "(?im)^(Microsoft Windows|Windows_NT) (?<Major>[\d]+)(\.(?<Minor>[\d]+))(\.(?<Build>[\d]+)).*$" {
            [int]$windowsMajorVersion = $Matches["Major"]
            [int]$windowsMinorVersion = $Matches["Minor"]
            [int]$windowsBuild = $Matches["Build"]
            [version]$windowsVersion = ("{0}.{1}.{2}" -f $Matches["Major"],$Matches["Minor"],$Matches["Build"])
            Write-Debug "Windows: '$OSDescription'"
            Write-Debug "Windows $($windowsVersion.ToString())"
            # Windows 10 and 11 both report NT version 10.0; distinguish by build number.
            # Note: osDescription usually lacks explicit SKU (Client/Server/Core/Nano), so rules are build-based.
            if (($windowsMajorVersion -eq 10) -and ($windowsMinorVersion -eq 0)) {
                if ($windowsBuild -ge 22631) {
                    # Windows 11 (22631+) and newer Windows Server families
                    $result.Reason = "Supported Windows build: ${windowsVersion}"
                    $result.V5AgentSupportsOS = $true
                    return $result
                }
                if (($windowsBuild -ge 22000) -and ($windowsBuild -lt 22631)) {
                    # Windows 11 below required client baseline
                    $result.Reason = "Unsupported Windows 11 build (< 22631): ${windowsVersion}"
                    $result.V5AgentSupportsOS = $false
                    $result.V5AgentSupportsOSText = "UnsupportedOSVersion"
                    return $result
                }
                if ($windowsBuild -ge 14393) {
                    # Windows 10 (1607+) and Windows Server / Server Core / Nano Server (2016+)
                    $result.Reason = "Supported Windows build: ${windowsVersion}"
                    $result.V5AgentSupportsOS = $true
                    return $result
                }

                $result.Reason = "Unsupported Windows build: ${windowsVersion}"
                $result.V5AgentSupportsOS = $false
                $result.V5AgentSupportsOSText = "UnsupportedOSVersion"
                return $result
            }
            if ($windowsMajorVersion -eq 6) {
                # Windows 7 / 8 / Windows Server 2012 / 2012 R2 and older
                $result.Reason = "Windows major version 6 is not supported: ${windowsVersion}"
                $result.V5AgentSupportsOS = $false
                $result.V5AgentSupportsOSText = "UnsupportedOSVersion"
                return $result
            }
            $result.Reason = "Unknown Windows version: '${OSDescription}'"
            $result.V5AgentSupportsOSText = "UnknownOSVersion"
            return $result
        }
        default {
            $result.Reason = "Unknown operating system: '$OSDescription'"
            $result.V5AgentSupportsOSText = "UnknownOS"
            return $result
        }
    }
}

if (!$OS -and !$OrganizationUrl) {
    Get-Help $MyInvocation.MyCommand.Definition
    return
}

if ($OS) {
    # Process OS parameter set
    $OS | ForEach-Object {
        New-Object PSObject -Property @{
            OS = $_
        } | Set-Variable agent
        Classify-OS -AgentOS $_ -Agent $agent
        Write-Output $agent
    } | Filter-Agents -AgentFilter $Filter `
      | Format-Table -Property OS,`
                               @{Label="UpgradeStatement"; Expression={
                                if ($_.ValidationResult.V5AgentSupportsOS -eq $null) {
                                    "$($PSStyle.Formatting.Warning)$($_.ValidationResult.UpgradeStatement)$($PSStyle.Reset)"
                                } elseif ($_.ValidationResult.V5AgentSupportsOS) {
                                    $_.ValidationResult.UpgradeStatement
                                } else {
                                    "$($PSStyle.Formatting.Error)$($_.ValidationResult.UpgradeStatement)$($PSStyle.Reset)"
                                }                                                    
                               }}
      | Out-Host -Paging

    return
}

# Process pool parameter set
if (!$OrganizationUrl) {
    Write-Warning "OrganizationUrl is required. Please specify -OrganizationUrl or set the AZDO_ORG_SERVICE_URL environment variable."
    exit 1
}
$OrganizationUrl = $OrganizationUrl -replace "/$","" # Strip trailing '/'
if (!$Token) {
    Write-Warning "No access token found. Please specify -Token or set the AZURE_DEVOPS_EXT_PAT or AZDO_PERSONAL_ACCESS_TOKEN environment variable."
    exit 1
}
if (!(Get-Command az -ErrorAction SilentlyContinue)) {
    Write-Warning "Azure CLI not found. Please install it."
    exit 1
}
if (!(az extension list --query "[?name=='azure-devops'].version" -o tsv)) {
    Write-Host "Adding Azure CLI extension 'azure-devops'..."
    az extension add -n azure-devops -y
}

Write-Host "`n$($PSStyle.Formatting.FormatAccent)This script will process all self-hosted pools in organization '${OrganizationUrl}' to:$($PSStyle.Reset)"
Write-Host "$($PSStyle.Formatting.FormatAccent)- Create an aggregated list of agents filtered by '${Filter}'$($PSStyle.Reset)"
Write-Host "$($PSStyle.Formatting.FormatAccent)- Create a CSV export of that list (so you can walk away from the computer when this runs)$($PSStyle.Reset)"
Write-Host "$($PSStyle.Formatting.FormatAccent)- Show list of agents filtered by '${Filter}' (list repeated at the end of script output)$($PSStyle.Reset)"
Write-Host "$($PSStyle.Formatting.FormatAccent)Note the Pipeline agent has more context about the operating system of the host it is running on (e.g. 'lsb_release -a' output), and is able to make a better informed decision on whether to upgrade or not.$($PSStyle.Reset)"
if (!$Force) {
    # Prompt to continue
    $choices = @(
        [System.Management.Automation.Host.ChoiceDescription]::new("&Continue", "Process pools")
        [System.Management.Automation.Host.ChoiceDescription]::new("&Exit", "Abort")
    )
    $defaultChoice = 0
    $decision = $Host.UI.PromptForChoice("Continue", "Do you wish to proceed retrieving data for agents in all pools in '${OrganizationUrl}'?", $choices, $defaultChoice)

    if ($decision -eq 0) {
        Write-Host "$($choices[$decision].HelpMessage)"
    } else {
        Write-Host "$($PSStyle.Formatting.Warning)$($choices[$decision].HelpMessage)$($PSStyle.Reset)"
        exit                    
    }
}

Write-Host "`nAuthenticating to organization ${OrganizationUrl}..."
$Token | az devops login --organization $OrganizationUrl
az devops configure --defaults organization=$OrganizationUrl

if (!$PoolId) {
    Write-Host "Retrieving self-hosted pools for organization ${OrganizationUrl}..."
    az pipelines pool list --query "[?!isHosted].id" `
                           -o tsv `
                           | Set-Variable PoolId
}
$PoolId | Measure-Object `
        | Select-Object -ExpandProperty Count `
        | Set-Variable totalNumberOfPools


$script:allAgents = [System.Collections.ArrayList]@()
try {
    $poolIndex = 0;
    $totalNumberOfAgents = 0;
    $numberOfPoolsToProcess = [math]::min($MaxPools,$totalNumberOfPools)
    foreach ($individualPoolId in $PoolId) {
        $poolIndex++
        if ($poolIndex -gt $MaxPools) {
            break
        }
        $OuterLoopProgressParameters = @{
            ID               = 0
            Activity         = "Processing pools"
            Status           = "Pool ${poolIndex} of ${numberOfPoolsToProcess}"
            PercentComplete  =  ($poolIndex / $totalNumberOfPools) * 100
            CurrentOperation = 'OuterLoop'
        }
        Write-Progress @OuterLoopProgressParameters
        $agents = $null
        $poolUrl = ("{0}/_settings/agentpools?poolId={1}" -f $OrganizationUrl,$individualPoolId)
        Write-Verbose "Retrieving pool with id '${individualPoolId}' in (${OrganizationUrl})..."
        az pipelines pool show --id $individualPoolId `
                               --query "name" `
                               -o tsv `
                               | Set-Variable poolName
        
        Write-Host "Retrieving v2 and v3 agents for pool '${poolName}' (${poolUrl})..."
        Write-Debug "az pipelines agent list --pool-id ${individualPoolId} --include-capabilities --query `"[?starts_with(version,'2.') || starts_with(version,'3.')]`""
        az pipelines agent list --pool-id $individualPoolId `
                                --include-capabilities `
                                --query "[?starts_with(version,'2.') || starts_with(version,'3.')]" `
                                -o json `
                                | ConvertFrom-Json `
                                | Set-Variable agents
        if ($agents) {
            $agents | Measure-Object `
                    | Select-Object -ExpandProperty Count `
                    | Set-Variable totalNumberOfAgentsInPool
            $agentIndex = 0
            $agents | ForEach-Object {
                $agentIndex++
                $totalNumberOfAgents++          
                $osConsolidated = $_.osDescription
                $capabilityOSDescription = ("{0} {1}" -f $_.systemCapabilities."Agent.OS",$_.systemCapabilities."Agent.OSVersion")
                if ($capabilityOSDescription -and !$osConsolidated -and ![string]::IsNullOrWhiteSpace($capabilityOSDescription)) {
                    $osConsolidated = $capabilityOSDescription
                }
                Write-Debug "osConsolidated: ${osConsolidated}"
                Write-Debug "capabilityOSDescription: ${capabilityOSDescription}"
                Classify-OS -AgentOS $osConsolidated -Agent $_
                $agentUrl = "{0}/_settings/agentpools?agentId={2}&poolId={1}" -f $OrganizationUrl,$individualPoolId,$_.id
                $_ | Add-Member -NotePropertyName AgentUrl -NotePropertyValue $agentUrl
                $_ | Add-Member -NotePropertyName OS -NotePropertyValue $osConsolidated
                $_ | Add-Member -NotePropertyName PoolName -NotePropertyValue $poolName
            } 
            $agents | Filter-Agents -AgentFilter $Filter `
                    | Format-Table -Property @{Label="Name"; Expression={$_.name}},`
                                             OS,`
                                             AgentUrl

            $script:allAgents.Add($agents) | Out-Null
        } else {
            Write-Host "There are no agents in pool '${poolName}' (${poolUrl})"
        }
    }
} finally {
    Write-Progress Id 0 -Completed
    Write-Progress Id 1 -Completed

    
    $script:allAgents | ForEach-Object { # Flatten nested arrays
                            $_ 
                        } `
                      | Set-Variable allAgents -Scope script

    $script:allAgents | Sort-Object -Property @{Expression = {$_.ValidationResult.SortOrder}; Descending = $false}, `
                                              @{Expression = "PoolName"; Descending = $false}, `
                                              @{Expression = "name"; Descending = $false} `
                      | Set-Variable allAgents -Scope script
    
    $exportFilePath = (Join-Path ([System.IO.Path]::GetTempPath()) "$([guid]::newguid().ToString()).csv")
    $script:allAgents | Filter-Agents -AgentFilter $Filter `
                      | Select-Object -Property @{Label="Name"; Expression={$_.name}},`
                                                @{Label="Id"; Expression={$_.id}},`
                                                @{Label="OS"; Expression={$_.OS -replace ";",""}},`
                                                @{Label="V5OS"; Expression={$_.ValidationResult.V5AgentSupportsOSText}},`
                                                @{Label="UpgradeStatement"; Expression={$_.ValidationResult.UpgradeStatement}},`
                                                @{Label="Reason"; Expression={$_.ValidationResult.Reason}},`
                                                @{Label="CreatedOn"; Expression={$_.createdOn}},`
                                                @{Label="StatusChangedOn"; Expression={$_.statusChangedOn}},`
                                                @{Label="Status"; Expression={$_.status}},`
                                                @{Label="Version"; Expression={$_.version}},`
                                                PoolName,`
                                                AgentUrl `
                      | Export-Csv -Path $exportFilePath
    if ($OpenCsv) {
        Open-Document -Document $exportFilePath
    }

    try {
        # Try block, in case the user cancels paging through results
        Write-Host "`nRetrieved agents with filter '${Filter}' in organization (${OrganizationUrl}) have been saved to ${exportFilePath}, and are repeated below"
        $script:allAgents | Filter-Agents -AgentFilter $Filter `
                          | Format-Table -Property @{Label="Name"; Expression={$_.name}},`
                                                   OS,`
                                                   @{Label="UpgradeStatement"; Expression={
                                                    if ($_.ValidationResult.V5AgentSupportsOS -eq $null) {
                                                        "$($PSStyle.Formatting.Warning)$($_.ValidationResult.UpgradeStatement)$($PSStyle.Reset)"
                                                    } elseif ($_.ValidationResult.V5AgentSupportsOS) {
                                                        $_.ValidationResult.UpgradeStatement
                                                    } else {
                                                        "$($PSStyle.Formatting.Error)$($_.ValidationResult.UpgradeStatement)$($PSStyle.Reset)"
                                                    }                                                    
                                                    }},`
                                                   @{Label="V5OS"; Expression={$_.ValidationResult.V5AgentSupportsOSText}},`
                                                   PoolName,`
                                                   AgentUrl `
                          | Out-Host -Paging
    
    } catch [System.Management.Automation.HaltCommandException] {
        Write-Warning "Skipped paging through results" 
    } finally {
        if ($script:allAgents) {
            Write-Host "`nRetrieved agents with filter '${Filter}' in organization (${OrganizationUrl}) have been saved to ${exportFilePath}"
            Write-Host "Processed ${totalNumberOfAgents} agents in ${totalNumberOfPools} in organization '${OrganizationUrl}'"
            $statisticsFilter = (($Filter -ieq "All") -or $IncludeMissingOSInStatistics ? "All" : "ExcludeMissingOS")
            Write-Host "`nAgents by v2/v3 -> v5 compatibility (${statisticsFilter}):"

            $script:allAgents | Filter-Agents -AgentFilter $statisticsFilter `
                              | Group-Object {$_.ValidationResult.V5AgentSupportsOSText} `
                              | Set-Variable agentsSummary
            $agentsSummary    | Measure-Object -Property Count -Sum | Select-Object -ExpandProperty Sum | Set-Variable totalNumberOfFilteredAgents
            $agentsSummary    | Format-Table -Property @{Label="V5AgentSupportsOS"; Expression={$_.Name}},`
                                                       Count,`
                                                       @{Label="Percentage"; Expression={($_.Count / $totalNumberOfFilteredAgents).ToString("p")}}
        }    
    }                    
}