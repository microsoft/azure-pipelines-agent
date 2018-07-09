# YAML getting started - Pools

When a job is started, it is assigned to run on a specific pool.

## Hosted pool

The VSTS hosted pool has several images to choose from:
- VS2017
- VS2015
- Xcode9
- Ubuntu18

```yaml
pool:
  name: Hosted
  image: VS2017
steps:
- script: echo hello world
```

## Private pools

Private pools support `demands`, which can be used to route the job to an available agent
within the pool. The demands are matched against agent capabilities.

For example:

```yaml
pool:
  name: MyPool
  demands: agent.os -equals Windows_NT
steps:
- script: echo hello world
```

Another example:

```yaml
pool:
  name: MyPool
  demands:
  - agent.os -equals Darwin
  - myCustomCapability -equals foo
steps:
- script: echo hello world
```

## Server pool

The `Server` pool is a special type of pool, for tasks that do not require an agent.
Only a subset of tasks can run on the server pool.

For example:

```yaml
pool: server
steps:
- task: InvokeRestApi@1
  inputs:
    serviceConnection: httpbin
    method: GET
    headers: '{ "Content-Type":"application/json" }'
    urlSuffix: get
```

## Authorization

For details about pool authorization, refer [here](yamlgettingstarted-authz.md).
