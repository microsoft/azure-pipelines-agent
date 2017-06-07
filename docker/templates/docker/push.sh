#!/bin/bash

VSTS_AGENT_TAG=$1

cd $(dirname $0)

while read version sha256; do
  docker push microsoft/vsts-agent:$VSTS_AGENT_TAG-docker-$version
  if [ "$VSTS_AGENT_TAG" == "auto" ]; then
    docker push microsoft/vsts-agent:docker-$version
  fi
done < <(cat docker.versions | sed 's/\r//')
