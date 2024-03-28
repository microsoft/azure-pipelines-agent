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

# fallback on Node16 if Node20 is not supported by the host
./externals/node20_1/bin/node --version
if [ $? == 0 ]; then
    NODE_VER="node20_1"
else    
    NODE_VER="node16"
fi

# run the host process which keep the listener alive
./externals/"$NODE_VER"/bin/node ./bin/AgentService.js &
PID=$!
wait $PID
trap - TERM INT
wait $PID
