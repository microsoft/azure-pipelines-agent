# Architectural Layers of the Agent Code

`Agent.Listener`, `Agent.Worker`, `Agent.PluginHost`, and `Agent.Plugins` are at the top.
They do not depend on each other.

`Agent.Listener` and `Agent.Worker` both depend on `Microsoft.VisualStudio.Services.Agent`.
(This could likely be renamed `Agent.Core` or `Agent.Common` for more clarity.)

All of the assemblies mentioned so far depend on `Agent.Sdk`, and many of them depend on the various `Microsoft.VisualStudio.Services.*` web APIs.
Additionally, `Agent.SDK` depends on some `Microsoft.TeamFoundation.*` assemblies.

## Diagram

In rough terms, dependencies look like this:

![Dependency graph](res/dependencies.svg)

```mermaid
graph TB
  subgraph App
  Agent.Listener
  Agent.Worker
  Agent.PluginHost
  Agent.Plugins
  end
  subgraph Platform
  agentcore[MS.VS.Services.Agent]
  agentsdk[Agent.SDK]
  end
  subgraph Infrastructure
  webapi[MS.VS.Services.*.WebAPI]
  tf[Microsoft.TeamFoundation.*]
  end
  Agent.Listener --> agentcore
  Agent.Worker --> agentcore
  Agent.PluginHost --> agentsdk
  Agent.Plugins --> agentsdk
  agentcore --> agentsdk
  agentcore --> webapi
  agentsdk --> tf
```