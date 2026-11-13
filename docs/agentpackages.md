# Agent Packages and Node versions

Agent tasks can be implemented in PowerShell or Node. The agent ships with multiple versions of Node that tasks can target.

As new Node versions are released, [tasks](https://github.com/microsoft/azure-pipelines-tasks) are updated to use new Node versions. The runtimes are included with the agent.

As Node versions exit out of the upstream maintenance window, some Pipelines tasks still depend on it. Azure DevOps updates supported tasks to a supported Node version. Third party tasks may still need older Node versions to run.

To accommodate this, we have 2 flavors of packages, subject to platform availability:

| Packages             | Node versions    | Description                |
|----------------------|------------------|----------------------------|
| `vsts-agent-*`       | 6, 10, 16, 20, 24 | Includes all Node versions that can be used as task execution handler |
| `pipelines-agent-*`  | 24               | Only the supported Node runner is bundled. Excludes Node 6, 10, 16, and 20. |

Tasks that depend on Node 6, 10, 16, or 20 require the `vsts-agent-*` package or a separately installed legacy runner at the expected agent path.
Node 6, 10, 16, and 20 are end-of-life. With the EOL policy disabled, `AGENT_WARN_ON_NODE20_TASK=true` enables a retirement warning when Node 20 is selected. Task maintainers should migrate to Node 24.
See [Node task runners](noderunner.md) for migration and legacy compatibility guidance.

Set `AGENT_RESTRICT_EOL_NODE_VERSIONS=true` to select Node 24 for agent-managed Node task runners. If Node 24 is unavailable or incompatible, selection fails without falling back to Node 20. An explicitly configured custom Node path can override this policy. With the policy disabled, tasks are not automatically upgraded to Node 24. Without an explicit override, a Node 20 task fails if its runner is missing or incompatible; it does not fall back to Node 16.

Node 24 has no official build for Windows x86 or Linux ARM32, so no `pipelines-agent` package is produced for these platforms. The `vsts-agent` package remains available for them; migrate to an operating system and architecture supported by Node 24.
