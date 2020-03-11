param (
    [Parameter(Mandatory = $true)]
    [string] $accountUrl,

    [Parameter(Mandatory = $true)]
    [string] $pat,

    [Parameter(Mandatory = $false)]
    [string] $continuationToken
)

# Create the VSTS auth header
$base64authinfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$pat"))
$vstsAuthHeader = @{"Authorization"="Basic $base64authinfo"}
$allHeaders = $vstsAuthHeader + @{"Content-Type"="application/json"; "Accept"="application/json"}

# List of deprecated images
[string[]] $deprecatedImages = 'WINCON', 'win1803', 'macOS-10.13', 'macOS 10.13', 'MacOS 1013', 'MacOS-1013', 'DefaultHosted', 'vs2015 win2012r2', 'vs2015-win2012r2'

try
{
    $result = Invoke-WebRequest -Headers $allHeaders -Method GET "$accountUrl/_apis/DistributedTask/pools?api-version=5.0-preview"
    if ($result.StatusCode -ne 200)
    {
        Write-Output $result.Content
        throw "Failed to query pools"
    }
    $resultJson = ConvertFrom-Json $result.Content
    $azurePipelinesPoolId = 0
    foreach($pool in $resultJson.value)
    {
        if ($pool.name -eq "Azure Pipelines")
        {
            $azurePipelinesPoolId = $pool.id
            break
        }
    }

    if ($azurePipelinesPoolId -eq 0)
    {
        throw "Failed to find Azure Pipelines pool"
    }
    
    Write-Host ("Azure Pipelines Pool Id: " + $azurePipelinesPoolId + "`n")

    $msg = 'Query next 200 jobs? (y/n)'
    $response = 'y'
    $hashJobsToDef = @{}
    do
    {
        if ($response -eq 'y')
        {
            Write-Output ("Querying next 200 jobs with continuation token:`n" + $continuationToken + "`n")

            if (!$continuationToken)
            {
                $result = Invoke-WebRequest -Headers $allHeaders -Method GET "$accountUrl/_apis/DistributedTask/pools/$($azurePipelinesPoolId)/jobrequests?api-version=5.0-preview&`$top=200"
            }
            else
            {
                $result = Invoke-WebRequest -Headers $allHeaders -Method GET "$accountUrl/_apis/DistributedTask/pools/$($azurePipelinesPoolId)/jobrequests?api-version=5.0-preview&`$top=200&continuationToken=$($continuationToken)"
            }

            if ($result.StatusCode -ne 200)
            {
                Write-Output $result.Content
                throw "Failed to query jobs"
            }
            $continuationToken = $result.Headers.'X-MS-ContinuationToken'
            $resultJson = ConvertFrom-Json $result.Content

            if ($resultJson.value.count -eq 0)
            {
                $response = 'n'
                Write-Output "Done`n"
                Write-Output "List of definitions targetting deprecated images:`n"
                Write-Output $hashJobsToDef
            }
            else
            {
                foreach($job in $resultJson.value)
                {
                    if ($job.agentSpecification -and
                        $job.agentSpecification.VMImage -and
                        $deprecatedImages.Contains($job.agentSpecification.VMImage))
                    {
                        $hashJobsToDef[$job.definition.name] = $job.definition._links.web.href
                    }
                }

                Write-Output "Current list of definitions targetting deprecated images:`n"
                Write-Output $hashJobsToDef
                Write-Output "`n"

                $response = Read-Host -Prompt $msg
            }
        }
    } until ($response -eq 'n')
}
catch {
    throw "Failed to query jobs: $_"
}