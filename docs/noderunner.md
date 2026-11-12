# Node task runners

Agent tasks can be implemented in PowerShell or Node. Use Node 24 as the runner for Node-based tasks.

## Node runner support

| Node version | Status |
|--------------|--------|
| 20 | End-of-life; included only in `vsts-agent` for backward compatibility. Migrate tasks to Node 24. |
| 24 | Recommended task runner. |

The `pipelines-agent` package bundles only Node 24 and is not produced for Windows x86 or Linux ARM32, where Node 24 has no official build; `vsts-agent` bundles Node 20 and Node 24, subject to platform availability. Missing legacy runners are not automatically downloaded.
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

Set `AGENT_RESTRICT_EOL_NODE_VERSIONS=true` to run tasks targeting end-of-life agent-managed Node handlers on Node 24. If Node 24 is unavailable or cannot run, the agent fails instead of falling back to Node 20 or an older runner. An explicitly configured custom Node path can override this policy. With the policy disabled, legacy handlers may still use a runner that is present in the selected agent package or manually installed.
On macOS agents and Windows agents running Linux containers, the policy requires the container image to declare a custom Node path because the agent's Node 24 binary cannot run in the container.
When the policy is disabled, a Node 20 task fails if its runner is missing or incompatible, unless an explicit override is configured. Manually installed Node 6, 10, and 16 runners remain available to tasks authored for those handlers.
With the EOL policy disabled, set `AGENT_WARN_ON_NODE20_TASK=true` to emit a Node 20 retirement warning when Node 20 is selected. This warning is disabled by default.

## Manual runner installation paths

The agent uses the following executable paths for legacy runners, relative to the agent's installation directory:

| Runner | Windows | Linux / macOS |
|--------|---------|---------------|
| Node 6 | `externals\node\bin\node.exe` | `externals/node/bin/node` |
| Node 10 | `externals\node10\bin\node.exe` | `externals/node10/bin/node` |
| Node 16 | `externals\node16\bin\node.exe` | `externals/node16/bin/node` |
| Node 20 | `externals\node20_1\bin\node.exe` | `externals/node20_1/bin/node` |

Use the matching Node major version for each folder. Installing a runner makes it available for task-handler selection; it does not force the agent to select it. These runners are end-of-life, and installing them does not restore upstream support.

Use a binary compatible with the agent's operating system and architecture, and ensure the agent account can execute it. Keep the executable at the exact path above; placing it directly in the runner folder or leaving an archive's top-level directory in place will not work.
