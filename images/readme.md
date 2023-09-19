# Docker Images for the Agent CI/CD Pipeline

## Docker Hub

In order to publish these images, you need to be a member of the organization `azpagentinfra`

## How to Build

```bash
docker build --tag "azpagentinfra/alpine:latest" ./images/alpine/
```

## How to Push

```bash
docker push "azpagentinfra/alpine:latest"
```
