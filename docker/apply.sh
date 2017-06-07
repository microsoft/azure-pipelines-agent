#!/bin/bash
set -e

cd "$(dirname $0)"

# Remove existing versioned folders
cd versioned
ls -F | grep /$ | xargs rm -rf
cd ..

# Remove existing auto docker folder
rm -rf auto/docker

# Determine VSTS agent versions
VSTS_AGENT_VERSIONS="$(cat versioned/versions | grep -v ^# | sed 's/\r//')"

# Write versioned folders
cd versioned
for version in $VSTS_AGENT_VERSIONS; do
  mkdir $version
  sed s/'$(VSTS_AGENT_VER)'/$version/ Dockerfile.template > $version/Dockerfile
  if [ -n "$(which unix2dos)" ]; then
    unix2dos -q $version/Dockerfile
  fi
  cp start.sh $version
done
cd ..

# Write template folders
cd templates
for template in $(ls); do
  cd $template
  # Write auto templates
  ./apply.sh auto ../../auto
  # Write versioned templates
  for version in $VSTS_AGENT_VERSIONS; do
    ./apply.sh $version ../../versioned/$version
  done
  cd ..
done