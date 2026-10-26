# Node task runners

Agent tasks can be implemented in PowerShell or Node. Use Node 24 as the runner for Node-based tasks.

## Node runner support

| Node version | Status |
|--------------|--------|
| 6, 10, 16, 20 | Deprecated; end-of-life upstream. Migrate tasks to Node 24. |
| 24 | Recommended task runner. |

Deprecated runners may remain in some agent packages for backward compatibility. Their presence does not imply ongoing upstream security support.
See [Agent packages and Node versions](agentpakcages.md) for the runtimes included in each package.

For the Node runner deprecation and removal schedule, see [Node.js runners in Azure Pipelines Agent](https://learn.microsoft.com/en-us/azure/devops/pipelines/agents/nodejs-runners?view=azure-devops).

## Migrate tasks to Node 24

Task maintainers should update task dependencies for Node 24 compatibility, declare the `Node24` execution handler in `task.json`, and test the task on Node 24:

```json
{
  "execution": {
    "Node24": {
      "target": "index.js"
    }
  }
}
```

Use an up-to-date agent on a platform that supports Node 24. Handler selection also depends on the agent's Node 24 rollout settings.
Pipeline users should update to a task version that supports Node 24, or ask the task maintainer to migrate.
Installing Node with `NodeTool` changes the version available to pipeline scripts; it does not change a task's execution handler.

The installation instructions below are for legacy compatibility only. Installing a deprecated runner does not restore upstream support and is not a substitute for migrating tasks to Node 24.

## Install Node 6 runner manually

For legacy Node 6 tasks, the runner must be present in the agent's `externals/node` directory. Node 6 is deprecated and no longer receives upstream security updates.

Please use the following steps to manually install the required runner:

1. Download the latest available version of Node 6 binaries for your operating system from the official Node [registry](https://nodejs.org/dist/).

1. Create a folder named `node` under the `agent/externals` directory, and extract downloaded Node binaries into that folder.

You can also use the following commands to install the Node 6 runner via the Powershell or Bash:

Windows:
```powershell
    $agentFolder = ""   // Specify the Azure DevOps Agent folder, e.g. C:\agents\my_agent
    $osArch = ""        // Specify the OS architecture, e.g. x64 / x86

    New-Item -Type Directory -Path "${agentFolder}\externals\node"

    Invoke-WebRequest -Uri "https://nodejs.org/dist/v6.17.1/win-${osArch}/node.exe" -OutFile "${agentFolder}\externals\node\node.exe"
    Invoke-WebRequest -Uri "https://nodejs.org/dist/v6.17.1/win-${osArch}/node.lib" -OutFile "${agentFolder}\externals\node\node.lib"
```

Linux / macOS:
```bash
    agent_folder=""   // Specify the Azure DevOps Agent folder, e.g. /home/user/agents/my_agent
    os_platform=""    // Specify the OS platform, e.g. linux / darwin
    os_arch=""        // Specify the OS architecture, e.g. x64 / x86

    mkdir "${agent_folder}/externals/node"

    wget -O "/tmp/node-v6.17.1-${os_platform}-${os_arch}.tar.gz" "https://nodejs.org/dist/v6.17.1/node-v6.17.1-${os_platform}-${os_arch}.tar.gz"

    tar -xvf "/tmp/node-v6.17.1-${os_platform}-${os_arch}.tar.gz" -C "${agent_folder}/externals/node/"
```

## Install Node runner via NodeTaskRunnerInstaller

For legacy tasks that cannot yet be migrated, you can use the Azure DevOps task [NodeTaskRunnerInstaller](https://github.com/microsoft/azure-pipelines-tasks/tree/master/Tasks/NodeTaskRunnerInstallerV0) to install a legacy runner via Azure DevOps CI.

The following example installs the deprecated Node 6 runner for legacy task compatibility only:

```yaml
  - task: NodeTaskRunnerInstaller@0
    inputs:
      runnerVersion: 6
```

Please, check more details in [NodeTaskRunnerInstaller task](https://learn.microsoft.com/azure/devops/pipelines/tasks/reference/node-task-runner-installer-v0) documentation.
