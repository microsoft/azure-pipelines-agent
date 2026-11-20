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

The agent no longer downloads or copies the legacy `vso-task-lib` compatibility library for TFS 2015 tasks. Tasks must provide their own dependencies.

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

Use an up-to-date agent on a platform that supports Node 24. With the EOL policy disabled, a Node 20 task handler uses Node 20 and fails if Node 20 is missing or cannot run, unless an explicit override is configured; a Node 24 handler tries Node 24 and then Node 20. Fallback changes the runtime, not the selected handler. If the required runtime cannot run, the task fails rather than falling back to a legacy runner. Global overrides and custom Node paths can change the selection.
Pipeline users should update to a task version that supports Node 24, or ask the task maintainer to migrate.
Installing Node with `NodeTool` changes the version available to pipeline scripts; it does not change a task's execution handler.

Set `AGENT_RESTRICT_EOL_NODE_VERSIONS=true` to run tasks targeting end-of-life agent-managed Node handlers on Node 24. If Node 24 is unavailable or cannot run, the agent fails instead of falling back to Node 20 or an older runner. An explicitly configured custom Node path can override this policy. With the policy disabled, legacy handlers may still use a runner that is present in the selected agent package or manually installed.
On macOS agents and Windows agents running Linux containers, the policy requires the container image to declare a custom Node path because the agent's Node 24 binary cannot run in the container.
When the policy is disabled, a Node 20 task fails if its runner is missing or incompatible, unless an explicit override is configured. Manually installed Node 6, 10, and 16 runners remain available to tasks authored for those handlers.
With the EOL policy disabled, set `AGENT_WARN_ON_NODE20_TASK=true` to emit a Node 20 retirement warning when Node 20 is selected. This warning is disabled by default.

Without EOL policy or a global Node override, Node 6, Node 10, and Node 16 handlers retain task-declared runner selection. `AGENT_USE_NODE24=true` upgrades all Node task handlers to Node 24 when it is available. With enhanced selection and EOL policy disabled, an unavailable Node 24 leaves legacy handlers using their manually installed runners, or failing if those runners are missing; enabling EOL policy instead fails the task. With EOL policy disabled, `AGENT_USE_NODE20_1=true` explicitly selects Node 20 and fails if it is missing or incompatible, subject to the glibc override below; it does not upgrade to Node 24 or fall back to a legacy runner. EOL policy takes precedence over that global Node 20 override. If both global overrides are enabled, `AGENT_USE_NODE24` takes precedence. In legacy selection, `AGENT_USE_NODE=LTS` uses Node 24 when a legacy task's declared runner is missing; `AGENT_USE_NODE=upgrade` retains its existing legacy-runner upgrade behavior.

When a Node 6 handler is selected, the agent always warns that it is end-of-life, including when a policy or override runs the task on a newer Node version.

`AGENT_USE_NODE20_IN_UNSUPPORTED_SYSTEM=true` and `AGENT_USE_NODE24_IN_UNSUPPORTED_SYSTEM=true` suppress fallback for the respective runner's recognized glibc compatibility errors. They do not select a runner or bypass other executable checks, and cannot make an incompatible runtime work. Container startup still requires a runnable Node executable.

Job-container startup also tries the agent's Node 24 runtime first, then Node 20. If neither can run and Node 16 has been installed manually in `externals/node16`, startup falls back to it (unless the EOL policy is enabled); otherwise it fails. With EOL policy enabled, only Node 24 is used for agent-managed startup. Explicit custom Node paths remain overrides; platforms requiring the container's own Node must declare that custom path when the policy is enabled.

## Manual runner installation paths

The agent uses the following executable paths for legacy runners, relative to the agent's installation directory:

| Runner | Windows | Linux / macOS |
|--------|---------|---------------|
| Node 6 | `externals\node\bin\node.exe` | `externals/node/bin/node` |
| Node 10 | `externals\node10\bin\node.exe` | `externals/node10/bin/node` |
| Node 16 | `externals\node16\bin\node.exe` | `externals/node16/bin/node` |
| Node 20 | `externals\node20_1\bin\node.exe` | `externals/node20_1/bin/node` |

Use the matching Node major version for each folder. Installing a runner makes it available for task-handler selection; it does not force the agent to select it or bypass the end-of-life Node restriction policy. These runners are end-of-life, and installing them does not restore upstream support.

Use a binary compatible with the agent's operating system and architecture, and ensure the agent account can execute it. Keep the executable at the exact path above; placing it directly in the runner folder or leaving an archive's top-level directory in place will not work.
