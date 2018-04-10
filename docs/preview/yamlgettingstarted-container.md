
# YAML getting started - YAML container support (not yet available, for discussion only) 
 
## Container

Container resource in yaml that allow an agent phase declare at runtime which container instance the step will use.

### Syntax

```yaml
resources:
  containers:
  - container: string # The container name, step will reference container by name.

    { string: object } # Any container data used by the container type.
```

Docker container syntax
```yaml
resources:
  containers:
  - container: string # The container name, step will reference container by name.    
    
    image: string # Docker image name

    endpoint: string # The private docker registry endpoint's name defined in VSTS

    options: string # Any extra options you want to add for container startup.
    
    localImage: true | false # Whether the image is locally built and don't pull from docker registry
    
    env:
      { string: string } # A dictionary of environment variables added during container creation
```

### Example

A simple container resource declaration may look like this:

```yaml
resources:
  containers:
  - container: dev1
    image: ubuntu:16.04
  - container: dev2
    image: private:ubuntu
    registry: privatedockerhub
  - container: dev3
    image: ubuntu:17.10
    options: --cpu-count 4
  - container: dev4
    image: ubuntu:17.10
    options: --hostname container-test --env test=foo --ip 192.168.0.1
    localImage: true
    env:
      envVariable1: envValue1
      envVariable2: envValue2
```

A simple build definition with phase using container may look like this:

```yaml
resources:
  containers:
  - container: dev1
    image: ubuntu:16.04
phases:
- phase: phase1
  queue:
    name: default
    container: dev1
  steps:
  - script: printenv
```

You can also apply matrix to container which may look like this:

```yaml
resources:
  containers:
  - container: dev1
    image: ubuntu:14.04
  - container: dev2
    image: ubuntu:16.04
phases:
- phase: phase1
  queue:
    name: default
    container: $[variables['runtimeContainer']]
    matrix:
      ubuntu14:
        runtimeContainer: dev1
      ubuntu16:
        runtimeContainer: dev2
  steps:
  - script: printenv
```
