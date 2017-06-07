#!/bin/bash

VSTS_AGENT_TAG=$1
BUILD_DIR=$2

cd $(dirname $0)

while read version sha256; do
  docker build -t microsoft/vsts-agent:$VSTS_AGENT_TAG-docker-$version $BUILD_DIR/docker/$version
  if [ "$VSTS_AGENT_TAG" == "auto" ]; then
    docker tag microsoft/vsts-agent:$VSTS_AGENT_TAG-docker-$version microsoft/vsts-agent:docker-$version
  fi
done < <(cat docker.versions | sed 's/\r//')
