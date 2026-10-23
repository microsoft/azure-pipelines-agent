#!/bin/bash

# convert SIGTERM signal to SIGINT
# for more info on how to propagate SIGTERM to a child process see: http://veithen.github.io/2014/11/16/sigterm-propagation.html
trap 'kill -INT $PID' TERM INT

if [ -f ".path" ]; then
    # configure
    export PATH=`cat .path`
    echo ".path=${PATH}"
fi

# insert anything to setup env when running as a service

# Prefer Node24, fall back to Node20, then Node16 only if present
./externals/node24/bin/node --version 2>/dev/null
if [ $? == 0 ]; then
    NODE_VER="node24"
else
    ./externals/node20_1/bin/node --version 2>/dev/null
    if [ $? == 0 ]; then
        NODE_VER="node20_1"
    elif [ -f "./externals/node16/bin/node" ]; then
        ./externals/node16/bin/node --version 2>/dev/null
        if [ $? == 0 ]; then
            NODE_VER="node16"
        else
            echo "ERROR: No compatible Node.js runtime found." >&2
            exit 1
        fi
    else
        echo "ERROR: No compatible Node.js runtime found. Requires Node 20 or later." >&2
        exit 1
    fi
fi

# run the host process which keep the listener alive
./externals/"$NODE_VER"/bin/node ./bin/AgentService.js &
PID=$!
wait $PID
trap - TERM INT
wait $PID
