# Node task runners

Agent tasks can be implemented in PowerShell or Node. Use Node 24 as the runner for Node-based tasks.

## Node runner support

| Node version | Status |
|--------------|--------|
| 20 | End-of-life; migrate tasks to Node 24. |
| 24 | Recommended task runner. |

Only Node 20 and Node 24 are bundled, subject to platform availability. Older runners are not bundled or automatically downloaded.
See [Agent packages and Node versions](agentpakcages.md) for the runtimes included in each package.

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

Use an up-to-date agent on a platform that supports Node 24. Node 20 and Node 24 task handlers run on Node 24 by default, falling back to Node 20 only when Node 24 is missing or cannot run. If neither runtime can run, the task fails rather than falling back to a legacy runner.
Pipeline users should update to a task version that supports Node 24, or ask the task maintainer to migrate.
Installing Node with `NodeTool` changes the version available to pipeline scripts; it does not change a task's execution handler.

Set `AGENT_RESTRICT_EOL_NODE_VERSIONS=true` to run tasks targeting end-of-life Node handlers on Node 24. If Node 24 is unavailable or cannot run, the agent falls back to Node 20, and fails if Node 20 is also unavailable; it never falls back to an older runner. With the policy disabled, legacy handlers may still use a runner that is present in the selected agent package or manually installed.

Without EOL policy or a global Node override, Node 6, Node 10, and Node 16 handlers retain task-declared runner selection. `AGENT_USE_NODE24=true` upgrades all Node task handlers to Node 24 with Node 20 fallback. `AGENT_USE_NODE20_1=true` explicitly selects Node 20 and fails if it is missing or incompatible, subject to the glibc override below; it does not upgrade to Node 24 or fall back to a legacy runner. If both global overrides are enabled, `AGENT_USE_NODE24` takes precedence. In legacy selection, `AGENT_USE_NODE=LTS` uses Node 24 when a legacy task's declared runner is missing; `AGENT_USE_NODE=upgrade` retains its existing legacy-runner upgrade behavior.

When a Node 6 handler is selected, the agent always warns that it is end-of-life, including when a policy or override runs the task on a newer Node version.

`AGENT_USE_NODE20_IN_UNSUPPORTED_SYSTEM=true` and `AGENT_USE_NODE24_IN_UNSUPPORTED_SYSTEM=true` suppress fallback for the respective runner's recognized glibc compatibility errors. They do not select a runner or bypass other executable checks, and cannot make an incompatible runtime work. Container startup still requires a runnable Node executable.

Job-container startup also tries the agent's Node 24 runtime first, then Node 20. If neither can run and Node 16 has been installed manually in `externals/node16`, startup falls back to it (unless the EOL policy is enabled); otherwise it fails. Explicit custom Node paths and platforms that require the container's own Node remain unchanged.

## Manual runner installation paths

The agent uses the following executable paths for legacy runners, relative to the agent's installation directory:

| Runner | Windows | Linux / macOS |
|--------|---------|---------------|
| Node 6 | `externals\node\bin\node.exe` | `externals/node/bin/node` |
| Node 10 | `externals\node10\bin\node.exe` | `externals/node10/bin/node` |
| Node 16 | `externals\node16\bin\node.exe` | `externals/node16/bin/node` |

Use the matching Node major version for each folder. Installing a runner makes it available for task-handler selection; it does not force the agent to select it or bypass the end-of-life Node restriction policy. These runners are end-of-life, and installing them does not restore upstream support.

Use a binary compatible with the agent's operating system and architecture, and ensure the agent account can execute it. Keep the executable at the exact path above; placing it directly in the runner folder or leaving an archive's top-level directory in place will not work.
