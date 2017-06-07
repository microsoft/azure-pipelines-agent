#!/bin/bash
set -e

cd "$(dirname $0)"

# Build the auto (and latest) image
cd auto
docker build -t microsoft/vsts-agent:auto -t microsoft/vsts-agent:latest .
cd ..

# Determine VSTS agent versions
VSTS_AGENT_VERSIONS="$(cat versioned/versions | grep -v ^# | sed 's/\r//')"

# Build versioned images
cd versioned
for version in $VSTS_AGENT_VERSIONS; do
  cd $version
  docker build -t microsoft/vsts-agent:$version .
  cd ..
done
cd ..

# Build derived images
cd templates
for template in $(ls); do
  cd $template
  # Build auto derived images
  ./build.sh auto ../../auto
  # Build versioned derived images
  for version in $VSTS_AGENT_VERSIONS; do
    ./build.sh $version ../../versioned/$version
  done
  cd ..
done