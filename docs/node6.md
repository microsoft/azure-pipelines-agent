# Node 6 and Agent Packages

Agent tasks can be implemented in PowerShell or Node. The agent currently ships with two versions of Node that tasks can target: 6 & 10.

Node 6 has long since passed out of the upstream maintenance window, however many Pipelines tasks depend on it. Azure DevOps is currently in the process of migrating all offically supported tasks to Node 10. Third party tasks will need to be updated by their maintainers to migrate to Node 10.

For these reasons, packages of the agent (named vsts-agent-*) that include Node 6 will continue to be made available for the forseeable future. Node 6 dependent tasks will continue be supported in hosted pools.

However, because Node 6 is no longer maintained, many customers do not want it installed on their systems. For customers who know for sure they are not using Node 6 dependent tasks, we provide alternate packages (pipelines-agent-*) that only include Node 10. In the future, once all officially supported tasks have been updated for Node 10, these packages will become the primarily recommended ones.
