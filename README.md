# Azure Pipelines Agent

## Announcement -  `AZP_AGENT_USE_LEGACY_HTTP` agent knob future deprecation

We are working on pipeline agent migration to .NET 6. One of the side effect of this migration is that the legacy HTTP handler will be no longer available for use due to changes in the .NET runtime itself.

Thus the related agent knob will not work once the migration will be completed. We recommend stopping using the `AZP_AGENT_USE_LEGACY_HTTP` knob.

## Overview

The cross-platform build and release agent for Azure Pipelines and Team Foundation Server 2015 and beyond.
This replaced the deprecated closed source windows build agent and the previous [cross-platform agent](https://github.com/Microsoft/vso-agent).

Supported on Windows, macOS, and several Linux flavors.
Written for .NET Core in C#.

## Status

|   | Build & Test |
|---|:-----:|
|![Win-x64](docs/res/win_med.png) **Windows x64**|[![Build & Test][win-x64-build-badge]][build]| 
|![Win-x86](docs/res/win_med.png) **Windows x86**|[![Build & Test][win-x86-build-badge]][build]| 
|![macOS](docs/res/apple_med.png) **macOS**|[![Build & Test][macOS-build-badge]][build]| 
|![Linux-x64](docs/res/linux_med.png) **Linux x64**|[![Build & Test][linux-x64-build-badge]][build]|
|![Linux-arm](docs/res/linux_med.png) **Linux ARM**|[![Build & Test][linux-arm-build-badge]][build]|
|![RHEL6-x64](docs/res/redhat_med.png) **RHEL 6 x64**|[![Build & Test][rhel6-x64-build-badge]][build]|

[win-x64-build-badge]: https://mseng.visualstudio.com/pipelinetools/_apis/build/status/VSTS.Agent/azure-pipelines-agent.ci?branchName=master&jobname=Windows%20(x64)
[win-x86-build-badge]: https://mseng.visualstudio.com/pipelinetools/_apis/build/status/VSTS.Agent/azure-pipelines-agent.ci?branchName=master&jobname=Windows%20(x86)
[macOS-build-badge]: https://mseng.visualstudio.com/pipelinetools/_apis/build/status/VSTS.Agent/azure-pipelines-agent.ci?branchName=master&jobname=macOS%20(x64)
[linux-x64-build-badge]: https://mseng.visualstudio.com/pipelinetools/_apis/build/status/VSTS.Agent/azure-pipelines-agent.ci?branchName=master&jobname=Linux%20(x64)
[linux-arm-build-badge]: https://mseng.visualstudio.com/pipelinetools/_apis/build/status/VSTS.Agent/azure-pipelines-agent.ci?branchName=master&jobname=Linux%20(ARM)
[rhel6-x64-build-badge]: https://mseng.visualstudio.com/pipelinetools/_apis/build/status/VSTS.Agent/azure-pipelines-agent.ci?branchName=master&jobname=RHEL6%20(x64)
[build]: https://mseng.visualstudio.com/PipelineTools/_build?_a=completed&definitionId=7502

## Get the Agent

[Get started with the agent](https://docs.microsoft.com/azure/devops/pipelines/agents/agents?view=azure-devops#install).

## Supported Usage

This agent can be used for both Azure Pipelines and Azure DevOps Server (Team Foundation Server).
Support is extended to all on-premise solutions [based on their lifecycle (including extended support)](https://learn.microsoft.com/lifecycle/products/).

The only exception is the Windows version of the agent for TFS 2015 since it is distributed along with a separate Node-based agent.

## Latest and Pre-release labels for releases

Releases have labels **Latest** and **Pre-release**. Please make a note that the labels mean:
- **Latest** - this is current agent release, available for all customers or partially (in case if this is active release, which is under roll out right now).
- **Pre-release** - Next version, release process of the agent version was started and it's already available for some of the users. And will be available soon for the rest.

Each new version of agent is released for users by groups during several days. And usually it becomes available for all users within 6-8 days after start of release. The release has label "Pre-release" during all these days. So it's expected behavior if specific release is used by builds in pipelines but it's marked as "Pre-release".

## Troubleshooting

Troubleshooting tips are [located here](docs/troubleshooting.md)

## Contribute

For developers that want to contribute, [read here](docs/contribute.md) on how to build and test.

## Issues

We accept issue reports both here (file a GitHub issue) and in [Developer Community](https://developercommunity.visualstudio.com/spaces/21/index.html).

Do you think there might be a security issue? Have you been phished or identified a security vulnerability? Please don't report it here - let us know by sending an email to secure@microsoft.com.
