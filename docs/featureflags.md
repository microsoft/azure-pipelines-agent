# Agent feature flags
## Overview
The feature flags can adjust behavior in specific cases

## Usage
To use the flag in the pipeline you need to [define variable](https://learn.microsoft.com/en-us/azure/devops/pipelines/process/variables) with specific name. 

Please note that "Environment name" column may differ from "Template name".

For the [environment variables](https://learn.microsoft.com/en-us/azure/devops/pipelines/process/variables?view=azure-devops&tabs=yaml%2Cbatch#environment-variables) see "Environment name" column.

Example in yaml: 
```
  ...
  variables:
      agent.preferPowerShellOnContainers: true
  ...
```

For the [user-defined variables](https://learn.microsoft.com/en-us/azure/devops/pipelines/process/variables?view=azure-devops&tabs=yaml%2Cbatch#user-defined-variables) see "Template name" column.

Example command :
```
  // Powershell 
  $env:AGENT_PREFER_POWERSHELL_ON_CONTAINERS = 'true'

  // Bash 
  export AGENT_PREFER_POWERSHELL_ON_CONTAINERS='true'
```

## Flags description 
| Environment name | Template name | Default value | Description |
| :--------------- | :------------ | :------------ | :---------- |
| AGENT_PREFER_POWERSHELL_ON_CONTAINERS | agent.preferPowerShellOnContainers | true | If true, prefer using the PowerShell handler on Windows containers for tasks that provide both a Node and PowerShell handler version. |
| VSTS_SETUP_DOCKERGROUP | VSTS_SETUP_DOCKERGROUP | true | If true, allows the user to run docker commands without sudo |
| VSTS_SETUP_ALLOW_MOUNT_TASKS_READONLY | VSTS_SETUP_ALLOW_MOUNT_TASKS_READONLY | true | If true, allows the user to mount 'tasks' volume read-only on Windows OS |
| AGENT_SKIP_POST_EXECUTION_IF_CONTAINER_STOPPED | AGENT_SKIP_POST_EXECUTION_IF_CONTAINER_STOPPED | false | If true, skips post-execution step for tasks in case the target container has been stopped |
| AGENT_DOCKER_MTU_VALUE |  -  |  | Allow to specify MTU value for networks used by container jobs (useful for docker-in-docker scenarios in k8s cluster). |
| AZP_AGENT_DOCKER_NETWORK_CREATE_DRIVER | agent.DockerNetworkCreateDriver |  | Allow to specify which driver will be used when creating docker network |
| AZP_AGENT_DOCKER_ADDITIONAL_NETWORK_OPTIONS | agent.DockerAdditionalNetworkOptions |  | Allow to specify additional command line options to 'docker network' command when creating network for new containers |
| AZP_AGENT_USE_HOST_GROUP_ID | agent.UseHostGroupId | true | If true, use the same group ID (GID) as the user on the host on which the agent is running |
| agent.ToolsDirectory |  -  |  | The location to look for/create the agents tool cache |
| VSTS_OVERWRITE_TEMP | VSTS_OVERWRITE_TEMP | false | If true, the system temp variable will be overriden to point to the agent's temp directory. |
| VSTS_DISABLEFETCHBYCOMMIT | VSTS.DisableFetchByCommit | false | If true and server supports it, fetch the target branch by commit. Otherwise, fetch all branches and pull request ref to get the target branch. |
| VSTS_DISABLEFETCHPRUNETAGS | VSTS.DisableFetchPruneTags | false | If true, disable --prune-tags in the fetches. |
| system.prefergitfrompath | system.prefergitfrompath | false | Determines which Git we will use on Windows. By default, we prefer the built-in portable git in the agent's externals folder, setting this to true makes the agent find git.exe from %PATH% if possible. |
| VSTS_DISABLE_GIT_PROMPT | VSTS_DISABLE_GIT_PROMPT | true | If true, git will not prompt on the terminal (e.g., when asking for HTTP authentication). |
| AGENT_GIT_USE_SECURE_PARAMETER_PASSING | agent.GitUseSecureParameterPassing | true | If true, don't pass auth token in git parameters |
| AGENT_TFVC_USE_SECURE_PARAMETER_PASSING | agent.TfVCUseSecureParameterPassing | true | If true, don't pass auth token in TFVC parameters |
| AGENT_SOURCE_CHECKOUT_QUIET | agent.source.checkout.quiet | false | Aggressively reduce what gets logged to the console when checking out source. |
| AGENT_USE_NODE10 | AGENT_USE_NODE10 | false | Forces the agent to use Node 10 handler for all Node-based tasks |
| VSTS_AGENT_PERFLOG |  -  |  | If set, writes a perf counter trace for the agent. Writes to the location set in this variable. |
| VSTSAGENT_TRACE |  -  |  | If set to anything, trace level will be verbose |
| VSTSAGENT_DUMP_JOB_EVENT_LOGS | VSTSAGENT_DUMP_JOB_EVENT_LOGS | false | If true, dump event viewer logs |
| AGENT_DIAGLOGPATH |  -  |  | If set to anything, the folder containing the agent diag log will be created here. |
| WORKER_DIAGLOGPATH |  -  |  | If set to anything, the folder containing the agent worker diag log will be created here. |
| VSTS_AGENT_CHANNEL_TIMEOUT |  -  | 30 | Timeout for channel communication between agent listener and worker processes. |
| AZP_AGENT_DOWNLOAD_TIMEOUT |  -  | 1500 | Amount of time in seconds to wait for the agent to download a new version when updating |
| VSTS_TASK_DOWNLOAD_TIMEOUT |  -  | 1200 | Amount of time in seconds to wait for the agent to download a task when starting a job |
| VSTS_TASK_DOWNLOAD_RETRY_LIMIT |  -  | 3 | Attempts to download a task when starting a job |
| AZP_AGENT_USE_LEGACY_HTTP |  -  | false | Use the libcurl-based HTTP handler rather than .NET's native HTTP handler, as we did on .NET Core 2.1 |
| VSTS_HTTP_RETRY |  -  | 3 | Number of times to retry Http requests |
| VSTS_HTTP_TIMEOUT |  -  | 100 | Timeout for Http requests |
| VSTS_AGENT_HTTPTRACE |  -  | false | Enable http trace if true |
| no_proxy |  -  |  | Proxy bypass list if one exists. Should be comma seperated |
| http_proxy |  -  |  | Proxy server address if one exists |
| VSTS_HTTP_PROXY_PASSWORD |  -  |  | Proxy password if one exists |
| VSTS_HTTP_PROXY_USERNAME |  -  |  | Proxy username if one exists |
| SYSTEM_UNSAFEALLOWMULTILINESECRET | SYSTEM_UNSAFEALLOWMULTILINESECRET | false | WARNING: enabling this may allow secrets to leak. Allows multi-line secrets to be set. Unsafe because it is possible for log lines to get dropped in agent failure cases, causing the secret to not get correctly masked. We recommend leaving this option off. |
| AZP_USE_CREDSCAN_REGEXES |  -  | false | Use the CredScan regexes for masking secrets. CredScan is an internal tool developed at Microsoft to keep passwords and authentication keys from being checked in. This defaults to disabled, as there are performance problems with some task outputs. |
| AZP_IGNORE_SECRETS_SHORTER_THAN | AZP_IGNORE_SECRETS_SHORTER_THAN | 0 | Specify the length of the secrets, which, if shorter, will be ignored in the logs. |
| AZP_AGENT_DOWNGRADE_DISABLED |  -  | false | Disable agent downgrades. Upgrades will still be allowed. |
| AGENT_TEST_VALIDATE_EXECUTE_PERMISSIONS_FAILSAFE |  -  | 100 | Maximum depth of file permitted in directory hierarchy when checking permissions. Check to avoid accidentally entering infinite loops. |
| DISABLE_INPUT_TRIMMING |  -  | false | By default, the agent trims whitespace and new line characters from all task inputs. Setting this to true disables this behavior. |
| DECODE_PERCENTS | DECODE_PERCENTS | true | By default, the agent does not decodes %AZP25 as % which may be needed to allow users to work around reserved values. Setting this to true enables this behavior. |
| ALLOW_TFVC_UNSHELVE_ERRORS | ALLOW_TFVC_UNSHELVE_ERRORS | false | By default, the TFVC unshelve command does not throw errors e.g. when there's no mapping for one or more files shelved. Setting this to true enables this behavior. |
| DISABLE_JAVA_CAPABILITY_HIGHER_THAN_9 |  -  |  | Recognize JDK and JRE >= 9 installed on the machine as agent capability. Setting any value to DISABLE_JAVA_CAPABILITY_HIGHER_THAN_9 is disabling this behavior |
| DISABLE_BUILD_ARTIFACTS_TO_BLOB | DISABLE_BUILD_ARTIFACTS_TO_BLOB | false | By default, the agent will upload build artifacts to Blobstore. Setting this to true will disable that integration. This variable is temporary and will be removed. |
| EnableIncompatibleBuildArtifactsPathResolution | EnableIncompatibleBuildArtifactsPathResolution | false | Return DownloadBuildArtifactsV1 target path resolution behavior back to how it was originally implemented. This breaks back compatibility with DownloadBuildArtifactsV0. |
| DISABLE_AUTHENTICODE_VALIDATION |  -  |  | Disables authenticode validation for agent package during self update. Set this to any non-empty value to disable. |
| DISABLE_HASH_VALIDATION |  -  | false | If true, the agent will skip package hash validation during self-updating. |
| ENABLE_VS_PRERELEASE_VERSIONS |  -  | false | If true, the agent will include to seach VisualStudio prerelease versions to capabilities. |
| DISABLE_OVERRIDE_TFVC_BUILD_DIRECTORY | DISABLE_OVERRIDE_TFVC_BUILD_DIRECTORY | false | Disables override of Tfvc build directory name by agentId on hosted agents (one tfvc repo used). |
| DISABLE_NODE6_DEPRECATION_WARNING | DISABLE_NODE6_DEPRECATION_WARNING | true | Disables Node 6 deprecation warnings. |
| DISABLE_TEE_PLUGIN_REMOVAL | DISABLE_TEE_PLUGIN_REMOVAL | false | Disables removing TEE plugin after using it during checkout. |
| TEE_PLUGIN_DOWNLOAD_RETRY_COUNT | TEE_PLUGIN_DOWNLOAD_RETRY_COUNT | 3 | Number of times to retry downloading TEE plugin |
| VSTSAGENT_DUMP_PACKAGES_VERIFICATION_RESULTS | VSTSAGENT_DUMP_PACKAGES_VERIFICATION_RESULTS | false | If true, dumps info about invalid MD5 sums of installed packages |
| VSTSAGENT_CONTINUE_AFTER_CANCEL_PROCESSTREEKILL_ATTEMPT | VSTSAGENT_CONTINUE_AFTER_CANCEL_PROCESSTREEKILL_ATTEMPT | false | If true, continue cancellation after attempt to KillProcessTree |
| AGENT_USE_NODE | AGENT_USE_NODE |  | Forces the agent to use different version of Node if when configured runner is not available. Possible values: LTS - make agent use latest LTS version of Node; UPGRADE - make agent use next available version of Node |