#!/bin/bash

function delay {
    time=${1:-1}
    if [ -x "$(command -v sleep)" ]; then
        sleep $time >/dev/null
    elif [ -x "$(command -v ping)" ]; then
        ping -n $time 127.0.0.1 >nul
    else
        count=0

        while [[ $count != $[$time*25000] ]]; do
            echo "sleep" >/dev/null
            count=$[$count+1]
        done
    fi
}

# Validate not sudo
user_id=`id -u`
if [ $user_id -eq 0 -a -z "$AGENT_ALLOW_RUNASROOT" ]; then
    echo "Must not run interactively with sudo"
    exit 1
fi

# Change directory to the script root directory
# https://stackoverflow.com/questions/59895/getting-the-source-directory-of-a-bash-script-from-within
SOURCE="${BASH_SOURCE[0]}"
while [ -h "$SOURCE" ]; do # resolve $SOURCE until the file is no longer a symlink
  DIR="$( cd -P "$( dirname "$SOURCE" )" && pwd )"
  SOURCE="$(readlink "$SOURCE")"
  [[ $SOURCE != /* ]] && SOURCE="$DIR/$SOURCE" # if $SOURCE was a relative symlink, we need to resolve it relative to the path where the symlink file was located
done
DIR="$( cd -P "$( dirname "$SOURCE" )" && pwd )"

# Do not "cd $DIR". For localRun, the current directory is expected to be the repo location on disk.

# Run
shopt -s nocasematch
if [[ "$1" == "localRun" ]]; then
    "$DIR"/bin/Agent.Listener $*
else
    "$DIR"/bin/Agent.Listener run $*

    # Return code 4 means the run once agent received an update message.
    # Sleep at least 5 seconds (to allow the update process to start) and
    # at most 20 seconds (to allow it to finish) then run the new agent
    # again.
    returnCode=$?
    if [[ $returnCode == 4 ]]; then
        delay 5

        retry=0
        while [[ $retry != 15 ]] && [ ! -x "$DIR"/bin/Agent.Listener ]; do
            delay 1
            retry=$[retry+1]
        done

        if [ ! -x "$DIR"/bin/Agent.Listener ]; then
            echo "Failed to update within 20 seconds." >&2
            exit 1
        fi
        
        "$DIR"/bin/Agent.Listener run $*
    else
        exit $returnCode
    fi
fi
