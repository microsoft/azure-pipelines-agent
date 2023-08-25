# Docker Images For Building Agent

## Docker Hub

In order to publish these images, you need to be a member of the organization `azpagentinfra`

## How to Build

```bash
docker build --tag azpagentinfra/alpine:latest ./images/alpine/
docker build --tag azpagentinfra/centos7:latest ./images/centos7/
```

## How to Publish

```bash
docker push azpagentinfra/alpine:latest
docker push azpagentinfra/centos7:latest
```
