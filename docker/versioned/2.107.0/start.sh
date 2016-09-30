#!/bin/bash

if [ $(id -u) -eq 0 ]; then
  if [ -n "$VSTS_WORK" ]; then
    mkdir -p "$VSTS_WORK"
    chown -R vsts:vsts "$VSTS_WORK"
  fi
  su vsts -s /bin/bash -c $0
  exit $?
fi

if [ -z "$VSTS_ACCOUNT" ]; then
  echo 1>&2 error: missing VSTS_ACCOUNT environment variable
  exit 1
fi
if [ -z "$VSTS_TOKEN" ]; then
  echo 1>&2 error: missing VSTS_TOKEN environment variable
  exit 1
fi

cd ~/agent

export VSO_AGENT_IGNORE=_,MAIL,PATH,VSO_AGENT_IGNORE,VSTS_AGENT_LOC,VSTS_AGENT_VER,VSTS_AGENT,VSTS_ACCOUNT,VSTS_TOKEN,VSTS_POOL,VSTS_WORK
if [ -n "$VSTS_AGENT_IGNORE" ]; then
  export VSO_AGENT_IGNORE=$VSO_AGENT_IGNORE,VSTS_AGENT_IGNORE,$VSTS_AGENT_IGNORE
fi

./config.sh --unattended \
  --agent $(eval echo ${VSTS_AGENT:-$(hostname)}) \
  --url https://${VSTS_ACCOUNT}.visualstudio.com \
  --auth PAT \
  --token $VSTS_TOKEN \
  --pool ${VSTS_POOL:-Default} \
  --work ${VSTS_WORK:-_work} \
  --replace

./run.sh
