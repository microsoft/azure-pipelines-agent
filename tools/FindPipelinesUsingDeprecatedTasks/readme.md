# Finding Pipelines Using Deprecated Tasks

This script scans Azure DevOps pipeline definitions for usage of deprecated tasks. It fetches the current list of deprecated tasks from the [azure-pipelines-tasks DEPRECATION.md](https://github.com/microsoft/azure-pipelines-tasks/blob/master/DEPRECATION.md) on GitHub, then checks each pipeline's YAML definition for references to those tasks. If no project is specified, all projects in the organization are scanned.

## QueryPipelinesForDeprecatedTasks.ps1
usage:
`.\QueryPipelinesForDeprecatedTasks.ps1 -accountUrl <Azure_DevOps_Organization_URL> -pat <PAT_Token> -project <Project_Name>`

This script requires a PAT token with read access on pipeline definitions.

### Parameters

| Parameter | Required | Description |
|-----------|----------|-------------|
| `-accountUrl` | Yes | The Azure DevOps organization URL (e.g. `https://dev.azure.com/myorg`) |
| `-pat` | Yes | A Personal Access Token with permissions to read pipeline definitions |
| `-project` | No | The Azure DevOps project name to scan. If omitted, all projects in the organization are scanned |
| `-outputCsv` | No | Path to export results as a CSV file |

### Examples

Scan a project for deprecated task usage:
`.\QueryPipelinesForDeprecatedTasks.ps1 -accountUrl https://dev.azure.com/myorg -pat $myPat -project MyProject`

Scan and export results to CSV:
`.\QueryPipelinesForDeprecatedTasks.ps1 -accountUrl https://dev.azure.com/myorg -pat $myPat -project MyProject -outputCsv results.csv`

Scan all projects in the organization:
`.\QueryPipelinesForDeprecatedTasks.ps1 -accountUrl https://dev.azure.com/myorg -pat $myPat`

### Output

The script will output:
- A summary of deprecated tasks found, grouped by task name with pipeline counts
- A detailed table listing each pipeline name, the deprecated task reference, and a URL to the pipeline
- Optionally, a CSV file with the full results if `-outputCsv` is specified
