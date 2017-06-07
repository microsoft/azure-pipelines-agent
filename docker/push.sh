#!/bin/bash
set -e

cd "$(dirname $0)"

# Push the auto (and latest) image
docker push microsoft/vsts-agent:auto
docker push microsoft/vsts-agent:latest

# Determine VSTS agent versions
VSTS_AGENT_VERSIONS="$(cat versioned/versions | grep -v ^# | sed 's/\r//')"

# Push versioned images
for version in $VSTS_AGENT_VERSIONS; do
  docker push microsoft/vsts-agent:$version
done

# Push derived images
cd templates
for template in $(ls); do
  cd $template
  # Push auto derived images
  ./push.sh auto
  # Push versioned derived images
  for version in $VSTS_AGENT_VERSIONS; do
    ./push.sh $version
  done
  cd ..
done