#!/bin/bash

VSTS_AGENT_TAG=$1
OUTPUT_DIR=$2

cd $(dirname $0)

# Determine docker-compose version
DOCKER_COMPOSE_VERSION="$(cat docker-compose.version | sed 's/\r//')"

while read version sha256; do
  mkdir -p $OUTPUT_DIR/docker/$version
  sed \
    -e s/'$(VSTS_AGENT_TAG)'/$VSTS_AGENT_TAG/ \
    -e s/'$(DOCKER_VERSION)'/$version/ \
    -e s/'$(DOCKER_SHA256)'/$sha256/ \
    -e s/'$(DOCKER_COMPOSE_VERSION)'/$DOCKER_COMPOSE_VERSION/ \
    Dockerfile.template > $OUTPUT_DIR/docker/$version/Dockerfile
  if [ -n "$(which unix2dos)" ]; then
    unix2dos -q $OUTPUT_DIR/docker/$version/Dockerfile
  fi
  cp docker-start.sh $OUTPUT_DIR/docker/$version
done < <(cat docker.versions | sed 's/\r//')
