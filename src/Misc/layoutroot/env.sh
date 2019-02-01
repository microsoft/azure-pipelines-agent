#!/bin/bash

varCheckList=(
    'LANG' 
    'JAVA_HOME' 
    'ANT_HOME' 
    'M2_HOME' 
    'ANDROID_HOME' 
    'GRADLE_HOME' 
    'NVM_BIN' 
    'NVM_PATH'
    'VSTS_HTTP_PROXY'
    'VSTS_HTTP_PROXY_USERNAME'
    'VSTS_HTTP_PROXY_PASSWORD'
    )

varCheckListRegex=(
    "JAVA_HOME_.*"
    )

envContents=""

if [ -f ".env" ]; then
    envContents=`cat .env`
else
    touch .env
fi

function writeVar()
{
    checkVar="$1"
    checkDelim="${1}="
    if test "${envContents#*$checkDelim}" = "$envContents"
    then
        if [ ! -z "${!checkVar}" ]; then
            echo "${checkVar}=${!checkVar}">>.env
        fi
    fi 
}

echo $PATH>.path

for var_name in ${varCheckList[@]}
do
    writeVar "${var_name}"
done

for var in $(compgen -e); do
    for pattern in ${varCheckListRegex[@]}
    do
        if [[ ${var} =~ ${pattern} ]]; then
            writeVar "${var}"
        fi
    done
done