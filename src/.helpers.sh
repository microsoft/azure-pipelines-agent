#!/bin/bash

function failed()
{
    local error=${1:-Undefined error}
    echo "Failed: $error" >&2
    popd
    exit 1
}

function warn()
{
    local error=${1:-Undefined error}
    echo "WARNING - FAILED: $error" >&2
}

function checkRC()
{
    local rc=$?
    if [ $rc -ne 0 ]; then
        failed "${1} Failed with return code $rc"
    fi
}

function heading()
{
    echo
    echo
    echo "-----------------------------------------"
    echo "  ${1}"
    echo "-----------------------------------------"
}
