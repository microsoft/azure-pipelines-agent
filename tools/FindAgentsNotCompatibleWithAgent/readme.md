# Finding Pipelines Targeting Retired Images
  
The Azure Pipeline agent v2 uses .NET 3.1 Core, while agent v3 runs on .NET 6. This means agent v3 will drop support for operating systems no longer supported by .NET 6. For more information on the v3 agent, go to [aka.ms/azdo-pipeline-agent-version](https://aka.ms/azdo-pipeline-agent-version).  
This script will predict whether an agent will be able to upgrade from v2 to v3, using the operating system information of the agent. Note the Pipeline agent itself has more context about the operating system of the host it is running on, and is able to make the best informed decision on whether to upgrade or not.

For more information, go to https://aka.ms/azdo-pipeline-agent-version.

## QueryAgentPoolsForCompatibleOS.ps1
usage:   
`.\QueryAgentPoolsForCompatibleOS.ps1 -OrganizationUrl <Azure_DevOps_Organization_URL> -Token <PAT_Token>`   
This script requires the [Azure CLI](https://aka.ms/install-azure-cli) to be installed locally, and a PAT token with read access on 'Agent Pools' scope.

This script will produce a list of agents with compatibility concerns at the end of the script output, as well as export that to a CSV file so it can be opened in e.g. Excel. If you are using Excel, you can force it to be opened automatically to [import results](https://support.microsoft.com/office/import-or-export-text-txt-or-csv-files-5250ac4c-663c-47ce-937b-339e391393ba) using the `-OpenCsv` switch:    
`.\QueryAgentPoolsForCompatibleOS.ps1 -OrganizationUrl <Azure_DevOps_Organization_URL> -Token <PAT_Token> -OpenCsv`

For additional parameters that filter the output e.g. by pool, type:   
`.\QueryAgentPoolsForCompatibleOS.ps1 -?`
