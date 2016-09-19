#!/bin/bash

if [ -e /var/run/docker.sock ]; then
  groupadd -g $(stat -c %g /var/run/docker.sock) docker
  usermod -aG docker vsts
fi

. ./start.sh
