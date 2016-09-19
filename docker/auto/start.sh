#!/bin/bash

if [ $(id -u) -eq 0 ]; then
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

mkdir ~/agent
cd ~/agent

echo Determining matching VSTS agent...
VSTS_AGENT_URL=$(curl -fSL \
  -u user:$VSTS_TOKEN \
  -H 'Accept:application/json;api-version=3.0-preview' \
  https://${VSTS_ACCOUNT}.visualstudio.com/_apis/distributedtask/packages/agent?platform=ubuntu.14.04-x64 \
  | python -c "import sys;import json; \
               obj = json.load(sys.stdin); \
               print(sorted(obj['value'], key=lambda elem: ( \
                 elem['version']['major'], \
                 elem['version']['minor'], \
                 elem['version']['patch']), reverse=True)[0]['downloadUrl'])")
if [ -z "$VSTS_AGENT_URL" ]; then
  echo 1>&2 error: failed to determine matching VSTS agent
  exit 1
fi

echo Downloading VSTS agent...
curl -fSL $VSTS_AGENT_URL -o agent.tgz
if [ ! -e agent.tgz ]; then
  echo 1>&2 error: failed to download VSTS agent
  exit 1
fi

echo Installing VSTS agent...
tar -xzf agent.tgz
rm agent.tgz

export VSO_AGENT_IGNORE=_,MAIL,PATH,VSO_AGENT_IGNORE,VSTS_AGENT,VSTS_ACCOUNT,VSTS_TOKEN,VSTS_POOL,VSTS_WORK
if [ -n "$VSTS_AGENT_IGNORE" ]; then
  export VSO_AGENT_IGNORE=$VSO_AGENT_IGNORE,VSTS_AGENT_IGNORE,$VSTS_AGENT_IGNORE
fi

if [ -n "$VSTS_WORK" ]; then
  mkdir -p "$VSTS_WORK"
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
