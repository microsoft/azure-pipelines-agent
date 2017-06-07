![](https://github.com/microsoft/vsts-agent/raw/master/docker/images/vsts.png)

## Visual Studio Team Services agent
This repository contains images for the Visual Studio Team Services (VSTS) agent that runs tasks as part of a build or release.

## Supported tags and `Dockerfile` links
- [`auto`](https://github.com/microsoft/vsts-agent/blob/master/docker/auto/Dockerfile), [`latest`](https://github.com/microsoft/vsts-agent/blob/master/docker/auto/Dockerfile) [(auto/Dockerfile)](https://github.com/microsoft/vsts-agent/blob/master/docker/auto/Dockerfile)
- [`auto-docker-1.11.2`](https://github.com/microsoft/vsts-agent/blob/master/docker/auto/docker/1.11.2/Dockerfile), [`docker-1.11.2`](https://github.com/microsoft/vsts-agent/blob/master/docker/auto/docker/1.11.2/Dockerfile) [(auto/docker/1.11.2/Dockerfile)](https://github.com/microsoft/vsts-agent/blob/master/docker/auto/docker/1.11.2/Dockerfile)
- [`auto-docker-1.12.1`](https://github.com/microsoft/vsts-agent/blob/master/docker/auto/docker/1.12.1/Dockerfile), [`docker-1.12.1`](https://github.com/microsoft/vsts-agent/blob/master/docker/auto/docker/1.12.1/Dockerfile) [(auto/docker/1.12.1/Dockerfile)](https://github.com/microsoft/vsts-agent/blob/master/docker/auto/docker/1.12.1/Dockerfile)
- [`2.106.0`](https://github.com/microsoft/vsts-agent/blob/master/docker/versioned/2.106.0/Dockerfile) [(2.106.0/Dockerfile)](https://github.com/microsoft/vsts-agent/blob/master/docker/versioned/2.106.0/Dockerfile)
- [`2.106.0-docker-1.11.2`](https://github.com/microsoft/vsts-agent/blob/master/docker/versioned/2.106.0/docker/1.11.2/Dockerfile) [(2.106.0/docker/1.11.2/Dockerfile)](https://github.com/microsoft/vsts-agent/blob/master/docker/versioned/2.106.0/docker/1.11.2/Dockerfile)
- [`2.106.0-docker-1.12.1`](https://github.com/microsoft/vsts-agent/blob/master/docker/versioned/2.106.0/docker/1.12.1/Dockerfile) [(2.106.0/docker/1.12.1/Dockerfile)](https://github.com/microsoft/vsts-agent/blob/master/docker/versioned/2.106.0/docker/1.12.1/Dockerfile)
- [`2.107.0`](https://github.com/microsoft/vsts-agent/blob/master/docker/versioned/2.107.0/Dockerfile) [(2.107.0/Dockerfile)](https://github.com/microsoft/vsts-agent/blob/master/docker/versioned/2.107.0/Dockerfile)
- [`2.107.0-docker-1.11.2`](https://github.com/microsoft/vsts-agent/blob/master/docker/versioned/2.107.0/docker/1.11.2/Dockerfile) [(2.107.0/docker/1.11.2/Dockerfile)](https://github.com/microsoft/vsts-agent/blob/master/docker/versioned/2.107.0/docker/1.11.2/Dockerfile)
- [`2.107.0-docker-1.12.1`](https://github.com/microsoft/vsts-agent/blob/master/docker/versioned/2.107.0/docker/1.12.1/Dockerfile) [(2.107.0/docker/1.12.1/Dockerfile)](https://github.com/microsoft/vsts-agent/blob/master/docker/versioned/2.107.0/docker/1.12.1/Dockerfile)

## How to use these images
There are two flavors of VSTS agent image: *auto* images, which automatically download the correct VSTS agent version to use at container startup, and *versioned* images, which come with specific VSTS agent versions. The auto images are preferable to ensure the correct version of the agent is chosen, and is reasonably used for scenarios where the container is long running. The versioned images can be used when the version is already known and container startup performance is more critical.

VSTS agents must be started with account connection information, which is provided through two environment variables:

- `VSTS_ACCOUNT`: the name of the Visual Studio account
- `VSTS_TOKEN`: a personal access token (PAT) for the Visual Studio account that has been given at least the **Agent Pools (read, manage)** scope.

To run an automatically versioned VSTS agent for a specific Visual Studio account:
```
docker run -e VSTS_ACCOUNT=<name> -e VSTS_TOKEN=<pat> -it microsoft/vsts-agent
```

VSTS agents can be further configured with additional environment variables:

- `VSTS_POOL`: the name of the agent pool (default: "Default")
- `VSTS_WORK`: the agent work folder (default: "_work")

To run a particular VSTS agent version for a specific account with a custom agent pool and a volume mapped agent work folder:
```
docker run -v /var/vsts:/var/vsts \
  -e VSTS_ACCOUNT=<name> -e VSTS_TOKEN=<pat> \
  -e VSTS_POOL=mypool -e VSTS_WORK /var/vsts \
  -it microsoft/vsts-agent:2.106.0
```

## Additional images
This repository also contains a set of additional images that extend the VSTS agent with capabilities that enable it to support many of the built-in VSTS build and release tasks.

### `docker` images
These base images include a version of the Docker CLI and the most recent version of the Docker Compose CLI. This image cannot run most of the built-in VSTS build or release tasks but it can run tasks that invoke arbitrary Docker workloads.

These images are not designed to run "docker in docker", but rather to re-use the host instance of Docker. To do this, volume map the host's Docker socket into the container:
```
docker run -v /var/run/docker.sock:/var/run/docker.sock \
  -e VSTS_ACCOUNT=<name> -e VSTS_TOKEN=<pat> \
  -it microsoft/vsts-agent:docker-1.11.2
```

### `standard` images
These images are based on the `docker` images and include a set of standard capabilities that enable many of the built-in VSTS build and release tasks as well as providing support for arbitrary Docker workloads.