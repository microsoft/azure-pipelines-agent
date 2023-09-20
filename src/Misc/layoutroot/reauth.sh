#!/bin/bash

user_id="$(id -u)"

# we want to snapshot the environment of the config user
if [ $user_id -eq 0 -a -z "$AGENT_ALLOW_RUNASROOT" ]; then
    echo "Must not run with sudo"
    exit 1
fi

# Check dotnet core 6.0 dependencies for Linux and FreeBSD
if [[ ($(uname) == "Linux") || ($(uname) == "FreeBSD") ]]; then
    if [ -e /etc/redhat-release ]; then
        redhatRelease=$(grep -oE "[0-9]+" /etc/redhat-release | awk "NR==1")
        if [[ "${redhatRelease}" -lt 7 ]]; then
            echo "RHEL supported for version 7 and higher."
            exit 1
        fi
    fi

    command -v ldd > /dev/null
    if [ $? -ne 0 ]; then
        echo "Can not find 'ldd'. Please install 'ldd' and try again."
        exit 1
    fi

    ldd ./bin/libcoreclr.so | grep -E "not found|No such"
    if [ $? -eq 0 ]; then
        echo "Dependencies is missing for .NET Core 6.0"
        echo "Execute ./bin/installdependencies.sh to install any missing dependencies."
        exit 1
    fi

    ldd ./bin/libSystem.Security.Cryptography.Native.OpenSsl.so | grep -E "not found|No such"
    if [ $? -eq 0 ]; then
        echo "Dependencies missing for .NET 6.0"
        echo "Execute ./bin/installdependencies.sh to install any missing dependencies."
        exit 1
    fi

    ldd ./bin/libSystem.IO.Compression.Native.so | grep -E "not found|No such"
    if [ $? -eq 0 ]; then
        echo "Dependencies missing for .NET 6.0"
        echo "Execute ./bin/installdependencies.sh to install any missing dependencies."
        exit 1
    fi

    if [ -e /etc/alpine-release ]; then
        if [ -z "$(apk info 2>&1 | grep icu-libs)" ]; then
            echo "icu-libs are missing"
            echo "Execute ./bin/installdependencies.sh to install any missing dependencies."
            exit 1
        fi
    else
        LDCONFIG="ldconfig"
        if ! [ -x "$(command -v $LDCONFIG)" ]; then
            LDCONFIG="/sbin/ldconfig"

            if ! [ -x "$LDCONFIG" ]; then
                echo "Can not find 'ldconfig' in PATH and '/sbin/ldconfig' doesn't exists either. Please install 'ldconfig' and try again."
                exit 1
            fi
        fi

        libpath="${LD_LIBRARY_PATH:-}"
        $LDCONFIG -NXv "${libpath//:/}" 2>&1 | grep libicu >/dev/null 2>&1
        if [ $? -ne 0 ]; then
            echo "libicu's dependencies missing for .NET 6"
            echo "Execute ./bin/installdependencies.sh to install any missing dependencies."
            exit 1
        fi
    fi
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
cd $DIR

source ./env.sh

shopt -s nocasematch
if [[ "$1" == "remove" ]]; then
    ./bin/Agent.Listener "$@"
else
    ./bin/Agent.Listener reauth "$@"
fi
