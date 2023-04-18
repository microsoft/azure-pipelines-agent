# Node 6 support

Agent tasks can be implemented in PowerShell or Node. The agent currently ships with three versions of Node that tasks can target: 6, 10 & 16.

Since Node 6 has long passed out of the upstream maintenance window, and all officially supported tasks are migrated from Node 6 to Node 10, Node 6 soon will be removed from the agent package. 
It's also highly recommended to third-party task maintainers migrate tasks to Node 10 or Node 16.

However, to support backward compatibility with the Node 6 tasks we provide self-service methods to install the designated Node runner manually.

## Install Node 6 runner manually

To support the execution of Node 6 tasks agent should be provided with the latest Node 6 version - `6.17.1.0`.

Despite that Node 6 is officially reached the End-of-Life, please, notice that it still can have maintenance updates, so it is required for the agent to get the latest binaries. You can check the currently existing Node versions [here](https://nodejs.org/dist/).

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

You can also use the Azure DevOps task [NodeTaskRunnerInstaller](https://github.com/microsoft/azure-pipelines-tasks/tree/master/Tasks/NodeTaskRunnerInstallerV0) to install the required runner version via Azure DevOps CI.

Use the following pipeline task sample to install the latest version of Node 6 runner:

```yaml
  - task: NodeTaskRunnerInstaller@0
    inputs:
      runnerVersion: 6
```

Please, check more details in [NodeTaskRunnerInstaller task]() documentation [TODO: FIX LINK].
