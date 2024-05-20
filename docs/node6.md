# Agent Packages and Node versions

Agent tasks can be implemented in PowerShell or Node. The agent ships with multiple versions of Node that tasks can target.

As new Node versions are released, [tasks](https://github.com/microsoft/azure-pipelines-tasks) are updated to use new Node versions. The runtimes are included with the agent.

As Node versions exit out of the upstream maintenance window, some Pipelines tasks still depend on it. Azure DevOps updates supported tasks to a supported Node version. Third party tasks may still need older Node versions to run.

To accommodate this, we have 2 flavors of packages:

| Packages             | Node versions | Description                |
|----------------------|---------------|----------------------------|
| `vsts-agent-*`       | 6, 10, 16, 20 | Includes all Node versions that can be used as task execution handler |
| `pipelines-agents-*` | 16, 20        | Includes only recent Node versions. The goal for these packages is to not include any end-of-life version of Node. |
