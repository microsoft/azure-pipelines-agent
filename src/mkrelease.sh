#!/bin/bash

###############################################################################
#
#  ./mkrelease.sh [version]
#        - Creates a release branch and updates necessary files for creating a
#          release named [version] of the agent
#
###############################################################################

set -e

NEW_RELEASE=$1

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EDITOR="${EDITOR:-vi}"
GIT_HUB_API_URL_ROOT="https://api.github.com/repos/microsoft/azure-pipelines-agent"
CURL=curl
NODE=node
GIT=git

# make sure we are clean
if [ -z "$(${GIT} status --untracked-files=no --porcelain)" ]; then
  echo "Git repo is clean."
else
  echo "You have uncommited changes in this clone. Aborting."
  exit -1
fi

RELEASE_TAG=v${NEW_RELEASE}
# verify $NEW_RELEASE is valid
if [[ "${NEW_RELEASE}" =~ ^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$ && ! "${NEW_RELEASE}" =~ ^.*\.999\.999$ ]];
then
  # Make sure version is not in use. This is needed because the release branch names have not always been consistent
  if [[ "$(${CURL} --silent ${GIT_HUB_API_URL_ROOT}/releases/tags/${RELEASE_TAG} | ${NODE} -pe "JSON.parse(fs.readFileSync(0)).message")" != "Not Found" ]];
  then
    echo "Version ${NEW_RELEASE} is already in use"
    exit -1
  fi
else
  echo "Invalid version '${NEW_RELEASE}'. Version must be in the form of <major>.<minor>.<patch> where each level is 0-999"
  exit -1
fi

# make release branch
NEW_BRANCH="releases/${NEW_RELEASE}"
${GIT} checkout -b ${NEW_BRANCH}

# edit agentversion
echo ${NEW_RELEASE} > ${SCRIPT_DIR}/agentversion

# fetch PRs
RELEASE_NOTES=$(cat ${SCRIPT_DIR}/../releaseNote.md)
# TODO: Update this REST call to only return PRs closed since last release (right now, it returns last 30 PRs closed)
PRS="$(${CURL} --silent "${GIT_HUB_API_URL_ROOT}/pulls?state=closed&sort=created&direction=desc" | ${NODE} -e "JSON.parse(fs.readFileSync(0)).forEach(function (item) { console.log(' - ' + item.title + ' (#' + item.number + ')');});")"
echo -e "${PRS}\n\n${RELEASE_NOTES}" > ${SCRIPT_DIR}/../releaseNote.md

# edit releaseNotes.md
${EDITOR} ${SCRIPT_DIR}/../releaseNote.md

# commit
${GIT} add ${SCRIPT_DIR}/agentversion
${GIT} add ${SCRIPT_DIR}/../releaseNote.md
${GIT} commit -m "Release ${NEW_RELEASE}"

# push
${GIT} push --set-upstream origin ${NEW_BRANCH}

# kick off release pipeline
# TODO: implement this

# create ADO L2 tests PR
# TODO: implement this

# create config change PR
# TODO: implement this
