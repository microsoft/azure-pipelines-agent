# Docker Images For Building Agent

## Docker Hub

In order to publish these images, you need to be a member of the organization `azpagentinfra`

## How to Build

```
% cd images
% docker build -t azpagentinfra/arm:latest arm
% docker build -t azpagentinfra/centos6:latest centos6
% docker build -t azpagentinfra/centos6:latest centos79
```

## How to Publish

```
% cd images
% docker push azpagentinfra/arm:latest
% docker push azpagentinfra/centos6:latest
% docker push azpagentinfra/centos79:latest
```
