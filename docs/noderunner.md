# Node task runners

Agent tasks can be implemented in PowerShell or Node. Use Node 24 as the runner for Node-based tasks.

## Node runner support

| Node version | Status |
|--------------|--------|
| 6, 10, 16, 20 | Deprecated; end-of-life upstream. Migrate tasks to Node 24. |
| 24 | Recommended task runner. |

Deprecated runners may remain in the `vsts-agent` package for backward compatibility. Their presence does not imply ongoing upstream security support.
The `pipelines-agent` package bundles only Node 24 and is not produced for Windows x86 or Linux ARM32, where Node 24 has no official build. Missing legacy runners are not automatically downloaded.
Container startup uses Node 24 when the agent's Node 20 runner is absent; the container image must support that runtime or provide its own Node.js through the `com.azure.dev.pipelines.agent.handler.node.path` image label.
See [Agent packages and Node versions](agentpackages.md) for the runtimes included in each package.

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
Set `AGENT_RESTRICT_EOL_NODE_VERSIONS=true` to run tasks targeting end-of-life agent-managed Node handlers on Node 24. If Node 24 is unavailable or cannot run, the agent fails instead of falling back to Node 20 or an older runner. An explicitly configured custom Node path can override this policy. With the policy disabled, legacy handlers may still use a runner that is present in the selected agent package or manually installed.
On macOS agents and Windows agents running Linux containers, the policy requires the container image to declare a custom Node path because the agent's Node 24 binary cannot run in the container.
When the policy is disabled, a Node 20 task fails if its runner is missing or incompatible, unless an explicit override is configured. With legacy node selection, `AGENT_USE_NODE=UPGRADE` can use a compatible Node 24 runner instead; it never falls back to Node 16 for a Node 20 task. Manually installed Node 6, 10, and 16 runners remain available to tasks authored for those handlers.
With the EOL policy disabled, set `AGENT_WARN_ON_NODE20_TASK=true` to emit a Node 20 retirement warning when a task authored for the Node 20 handler runs on Node 20. This warning is disabled by default. When a task that is not authored for Node 20 runs on Node 20 (because of the default runner selection, `AGENT_USE_NODE20_1`, `AGENT_USE_NODE=LTS|UPGRADE`, or Node 20 container startup), the agent always warns and explains why Node 20 was selected. Node 20 is never selected when its runner is absent, and the EOL policy never upgrades older tasks to Node 20.

## Manual runner installation paths

The agent uses the following executable paths for legacy runners, relative to the agent's installation directory:

| Runner | Windows | Linux / macOS |
|--------|---------|---------------|
| Node 6 | `externals\node\bin\node.exe` | `externals/node/bin/node` |
| Node 10 | `externals\node10\bin\node.exe` | `externals/node10/bin/node` |
| Node 16 | `externals\node16\bin\node.exe` | `externals/node16/bin/node` |
| Node 20 | `externals\node20_1\bin\node.exe` | `externals/node20_1/bin/node` |

Use the matching Node major version for each folder. Installing a runner makes it available to the agent; it does not force the agent to select it or bypass the end-of-life Node restriction policy. These runners are end-of-life, and installing them does not restore upstream support.

The examples below install Node 6. To install Node 10 or Node 16, replace the Node version in the download URL and archive name with the desired version, and replace the destination folder `node` with `node10` or `node16`, respectively. Keep the executable inside the destination's `bin` directory.

## Install Node 6 runner manually

For legacy Node 6 tasks, the runner must be present in the agent's `externals/node` directory. Node 6 is deprecated and no longer receives upstream security updates.

Please use the following steps to manually install the required runner:

1. Download the latest available version of Node 6 binaries for your operating system from the official Node [registry](https://nodejs.org/dist/).

1. Create a folder named `node` under the agent's `externals` directory. Place the executable at `externals\node\bin\node.exe` on Windows or `externals/node/bin/node` on Linux/macOS. When extracting a tar archive, remove its top-level directory so it does not introduce an extra directory between `node` and `bin`.

The agent looks for the executable at this exact path; placing it directly in `externals/node` or leaving the archive's top-level directory in place will not work. Use a binary compatible with the agent's operating system and architecture, and ensure the agent account can execute it.

You can also use the following commands to install the Node 6 runner via the Powershell or Bash:

Windows:
```powershell
    $agentFolder = ""   # Specify the Azure DevOps Agent folder, e.g. C:\agents\my_agent
    $osArch = ""        # Specify the OS architecture, e.g. x64 / x86

    New-Item -ItemType Directory -Force -Path "${agentFolder}\externals\node\bin"

    Invoke-WebRequest -Uri "https://nodejs.org/dist/v6.17.1/win-${osArch}/node.exe" -OutFile "${agentFolder}\externals\node\bin\node.exe"
    Invoke-WebRequest -Uri "https://nodejs.org/dist/v6.17.1/win-${osArch}/node.lib" -OutFile "${agentFolder}\externals\node\bin\node.lib"
```

Linux / macOS:
```bash
    agent_folder=""   # Specify the Azure DevOps Agent folder, e.g. /home/user/agents/my_agent
    os_platform=""    # Specify the OS platform, e.g. linux / darwin
    os_arch=""        # Specify the OS architecture, e.g. x64 / x86

    mkdir -p "${agent_folder}/externals/node"

    wget -O "/tmp/node-v6.17.1-${os_platform}-${os_arch}.tar.gz" "https://nodejs.org/dist/v6.17.1/node-v6.17.1-${os_platform}-${os_arch}.tar.gz"

    tar -xzvf "/tmp/node-v6.17.1-${os_platform}-${os_arch}.tar.gz" -C "${agent_folder}/externals/node/" --strip-components=1
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
