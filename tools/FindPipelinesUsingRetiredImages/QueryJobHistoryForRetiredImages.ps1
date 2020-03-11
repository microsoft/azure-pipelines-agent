param (
    $accountUrl,
    $pat
)

# Create the VSTS auth header
$base64authinfo = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes(":$pat"))
$vstsAuthHeader = @{"Authorization"="Basic $base64authinfo"}
$allHeaders = $vstsAuthHeader + @{"Content-Type"="application/json"; "Accept"="application/json"}

try
{
	$result = Invoke-WebRequest -Headers $allHeaders -Method GET "$accountUrl/_apis/DistributedTask/pools?api-version=5.0-preview"
	if ($result.StatusCode -ne 200)
    {
		echo $result.Content
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
    
    Write-Host ("Azure Pipelines Pool Id: " + $azurePipelinesPoolId)

    $msg = 'Query next 200 jobs? (y/n)'
    $response = 'y'
    $continuationToken = 0
    $hashJobsToDef = @{}
    do
    {
        if ($response -eq 'y')
        {
	        echo "Querying next 200 jobs"

            if ($continuationToken -eq 0)
            {
                $result = Invoke-WebRequest -Headers $allHeaders -Method GET "$accountUrl/_apis/DistributedTask/pools/$($azurePipelinesPoolId)/jobrequests?api-version=5.0-preview&`$top=200"
            }
            else
            {
                $result = Invoke-WebRequest -Headers $allHeaders -Method GET "$accountUrl/_apis/DistributedTask/pools/$($azurePipelinesPoolId)/jobrequests?api-version=5.0-preview&`$top=200&continuationToken=$($continuationToken)"
            }

	        if ($result.StatusCode -ne 200)
            {
		        echo $result.Content
		        throw "Failed to query jobs"
	        }
            $continuationToken = $result.Headers.'X-MS-ContinuationToken'
	        $resultJson = ConvertFrom-Json $result.Content

            if ($resultJson.value.count -eq 0)
            {
                $response = 'n'
                echo "Done"
                echo "List of definitions targetting deprecated images:"
                echo $hashJobsToDef
            }
            else
            {
                foreach($job in $resultJson.value)
                {
                    if ($job.agentSpecification)
                    {
                        if ($job.agentSpecification.VMImage -eq 'WINCON' -or
                            $job.agentSpecification.VMImage  -eq 'win1803' -or
                            $job.agentSpecification.VMImage  -eq 'macOS-10.13' -or
                            $job.agentSpecification.VMImage  -eq 'macOS 10.13' -or
                            $job.agentSpecification.VMImage  -eq 'MacOS 1013' -or
                            $job.agentSpecification.VMImage  -eq 'MacOS-1013' -or
                            $job.agentSpecification.VMImage  -eq 'DefaultHosted' -or
                            $job.agentSpecification.VMImage  -eq 'vs2015 win2012r2' -or
                            $job.agentSpecification.VMImage  -eq 'vs2015-win2012r2')
                        {
                            $hashJobsToDef[$job.definition.name] = $job.definition._links.web.href
                        }
                    }
                }

                echo "Current list of definitions targetting deprecated images:"
                echo $hashJobsToDef

                $response = Read-Host -Prompt $msg
            }
        }
    } until ($response -eq 'n')
}
catch {
	throw "Failed to query jobs: $_"
}