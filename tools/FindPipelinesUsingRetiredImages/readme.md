# Finding Pipelines Targeting Retired Images
  
Azure Pipelines will remove Windows Server 2016 with Visual Studio 2017(`vs2017-win2016`), macOS Mojave 10.14 (`macOS-10.14`) from our hosted pools.

The scripts in this directory are intended to help customers identify Pipelines that depend on these retired images. Customers can then navigate to and update those Pipelines.

## QueryJobHistoryForRetiredImages.ps1
usage:
`.\QueryJobHistoryForRetiredImages.ps1 <Azure_DevOps_Organization_URL> <PAT_Token>`

or optionally, you can pass in a continuation token from a previous run in case you need to pick up where you left off:
`.\QueryJobHistoryForRetiredImages.ps1 <Azure_DevOps_Organization_URL> <PAT_Token> <Continuation_Token>`

This script will query the "Azure Pipelines" Agent Pool's Job History and output unique Pipelines that targeted any of the retired images. It will query the jobs 200 at a time, as this is the REST API query limit, and prompt for continuation. This is to avoid account throttling in case of a large job history. It will output the current list of distinct Pipelines each iteration, with the URL to edit that Pipeline. It will also output once it has reached the end of the job history.