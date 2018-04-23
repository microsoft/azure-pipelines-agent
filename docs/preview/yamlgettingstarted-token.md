# YAML getting started - Allow scripts to access OAuth token

The OAuth token to communicate back to VSTS is available as a secret variable within a YAML build. The token can be use to authenticate to the [VSTS REST API](https://www.visualstudio.com/en-us/integrate/api/overview).

You can map the variable into the environment block for your script, or pass it via an input. The variables name can be arbitrary but in order to support e.g. old, existing `PowerShell` scripts, the variable name should be chosen to be `SYSTEM_ACCESSTOKEN` or similar for other task types.

For example:

```yaml
steps:
- powershell: |
    $url = "$($env:SYSTEM_TEAMFOUNDATIONCOLLECTIONURI)$env:SYSTEM_TEAMPROJECTID/_apis/build/definitions/$($env:SYSTEM_DEFINITIONID)?api-version=2.0"
    Write-Verbose "URL: $url" -Verbose
    $definition = Invoke-RestMethod -Uri $url -Headers @{
      Authorization = "Bearer $env:SYSTEM_ACCESSTOKEN"
    }
    Write-Verbose "Definition = $($definition | ConvertTo-Json -Depth 100)" -Verbose
  env:
    SYSTEM_ACCESSTOKEN: $(system.accesstoken)
```
