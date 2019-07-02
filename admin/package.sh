#!/usr/bin/env bash

# TODO:
# node handler uses private copy
# doc separate scenarios

# run to install
# curl -sSL https://raw.githubusercontent.com/Microsoft/vso-agent/master/getagent.sh | bash

DEFAULT_NODE_VERSION="5.6.0"
DEFAULT_TEE_VERSION="14.0.2-private"

# overrides so you can create an offline install for Linux from an OSX machine, etc...

# Darwin | Linux
platform=${1:-`uname`}

# x86_64
bitness=${2:-`uname -m`}

#no version is latest
DEFAULT_AGENT_VERSION=""

function failed()
{
   local error=${1:-Undefined error}
   echo "Failed: $error" >&2
   exit 1
}

function writeHeader() {
    echo 
    echo --------------------------------------
    echo "     ${1} "
    echo --------------------------------------
    echo
}

uid=`id -u`

writeHeader "Packaging $platform"

if [ $uid -eq 0 ]; then
    failed "Install cannot be run as root.  Do not use sudo"
fi

agent_version=${GET_AGENT_VERSION}

if [ ! $agent_version ]; then
    agent_version=$DEFAULT_AGENT_VERSION
fi

node_version=${GET_NODE_VERSION}

if [ ! $node_version ]; then
    node_version=$DEFAULT_NODE_VERSION
fi

tee_version=${GET_TEE_VERSION}

if [ ! $tee_version ]; then
    tee_version=$DEFAULT_TEE_VERSION
fi


function checkRC() {
    local rc=$?
    if [ $rc -ne 0 ]; then
        failed "${1} Failed with return code $rc"
    fi
}

function warnRC() {
    local rc=$?
    if [ $rc -ne 0 ]; then
        failed "WARNING: ${1} Failed with return code $rc.  ${2}"
    fi
}

# password early in script
mkdir -p _install

if [ -d "runtime" ]; then
    echo "removing existing runtime"
    rm -rf "runtime"
fi

# ------------------------------------------------------------
# Download Node
# ------------------------------------------------------------

writeHeader "Acquiring Node $node_version"
node_file='invalid'
if [[ "$platform" == "Darwin" ]]; then
    node_file="node-v${node_version}-darwin-x64"
elif [[ "$platform" == "Linux" ]]; then
    
    if [[ "$bitness" == "x86_64" ]]; then
        node_file="node-v${node_version}-linux-x64"
    else
        node_file="node-v${node_version}-linux-x86"
    fi
else
    failed 'Unsupported platform: $platform'
fi

zip_file=${node_file}.tar.gz
if [ -f ${zip_file} ]; then
    echo "Download exists"
else
    node_url="https://nodejs.org/dist/v${node_version}/${zip_file}"
    echo "Downloading Node ${node_version} @ ${node_url}"
    curl -skSLO $node_url &> _install/downloadnode.log
    checkRC "Download (curl)"
fi

if [ -d ${node_file} ]; then
    echo "Already extracted"
else
    tar zxvf ./${zip_file} &> _install/targznode.log
    checkRC "Unzip (node)"
fi

mkdir -p runtime/node
cp -R ${node_file}/. runtime/node

# ensure we use private node and npm for rest of script

echo "using node : `which node`"
echo "using npm  : `which npm`"

# ------------------------------------------------------------
# Download TEE CLI
# ------------------------------------------------------------
writeHeader "Acquiring TEE CLI $tee_version"
tee_file=TEE-CLC-${tee_version}
tee_zip=${tee_file}.zip

if [ -f ${tee_zip} ]; then
    echo "Download exists"
else
    echo "Downloading  TEE CLI ${tee_version}"
    curl -skSLO http://aka.ms/${tee_zip} &> _install/downloadtee.log
    warnRC "Download (TEE CLI)" "This is only critical if using TFSVC (not needed for git)"
fi

if [ -d ${tee_file} ]; then
    echo "Already extracted"
else
    unzip ./${tee_zip} &> _install/unziptee.log
    warnRC "Unzip (tee)" "This is only critical if using TFSVC (not needed for git)"
fi

mkdir -p runtime/tee
cp -R ${tee_file}/. runtime/tee

# ------------------------------------------------------------
# Create Agent
# ------------------------------------------------------------

writeHeader "Creating agent"
cp -R ../vsoxplat/ .

chmod 777 *.sh

npm install --production

# logging info for troubleshooting
find . > _install/layout.log
ls -la > _install/ls.log
cat package.json | grep "\"version" > _install/version.log
