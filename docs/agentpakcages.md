# Agent Packages and Node versions

Agent tasks can be implemented in PowerShell or Node. The agent ships with multiple versions of Node that tasks can target.

As new Node versions are released, [tasks](https://github.com/microsoft/azure-pipelines-tasks) are updated to use new Node versions. The runtimes are included with the agent.

As Node versions exit out of the upstream maintenance window, some Pipelines tasks still depend on it. Azure DevOps updates supported tasks to a supported Node version. Third party tasks may still need older Node versions to run.

The package flavors bundle different Node runners, subject to platform availability:

| Packages             | Node versions    | Description                |
|----------------------|------------------|----------------------------|
| `vsts-agent-*`       | 20, 24           | Node 20 is included for backward compatibility; use Node 24 for tasks. |
| `pipelines-agent-*`  | 24               | Only the supported Node runner is bundled. |

Legacy runners are not bundled or automatically downloaded. Tasks that still require an older runner can use a manually installed runner at the expected agent path.
Node 20 is deprecated. Task maintainers should migrate to Node 24.
See [Node task runners](noderunner.md) for migration and manual compatibility paths.

Set `AGENT_RESTRICT_EOL_NODE_VERSIONS=true` to run all Node task handlers on Node 24. If Node 24 is unavailable or incompatible, selection fails without falling back to Node 20. With the policy disabled, tasks are not automatically upgraded to Node 24: tasks that require a missing runner fail unless that runner is installed manually.

Node 24 is not bundled for Windows x86 or Linux ARM32. The `pipelines-agent` package on these platforms has no bundled Node task runner; migrate to an operating system and architecture supported by Node 24.
