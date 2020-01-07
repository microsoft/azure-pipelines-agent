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
INTEGRATION_DIR="${SCRIPT_DIR}/../_layout/integrations"
EDITOR="${EDITOR:-vi}"
GIT_HUB_API_URL_ROOT="https://api.github.com/repos/microsoft/azure-pipelines-agent"
GIT_HUB_SEARCH_API_URL_ROOT="https://api.github.com/search"
AGENT_RELEASE_PIPELINE_URL="https://dev.azure.com/mseng/AzureDevOps/_build?definitionId=5845"
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

# edit agentversion
echo ${NEW_RELEASE} > ${SCRIPT_DIR}/agentversion

# fetch PRs
LAST_RELEASE_DATE="$(${CURL} --silent ${GIT_HUB_API_URL_ROOT}/releases/latest | ${NODE} -pe "JSON.parse(fs.readFileSync(0)).published_at")"
echo "Fetching PRs merged since ${LAST_RELEASE_DATE}"
PRS="$(${CURL} --silent "${GIT_HUB_SEARCH_API_URL_ROOT}/issues?q=type:pr+is:merged+repo:microsoft/azure-pipelines-agent+merged:>=${LAST_RELEASE_DATE}&sort=closed_at&order=asc" | ${NODE} -e "JSON.parse(fs.readFileSync(0)).items.forEach(function (item) { console.log(' - ' + item.title + ' (#' + item.number + ')');});")"

echo -e "${PRS}\n\n" | cat - ${SCRIPT_DIR}/../releaseNote.md > ${SCRIPT_DIR}/../releaseNote.${NEW_RELEASE}.md

# edit releaseNotes.md
${EDITOR} ${SCRIPT_DIR}/../releaseNote.${NEW_RELEASE}.md
mv ${SCRIPT_DIR}/../releaseNote.${NEW_RELEASE}.md ${SCRIPT_DIR}/../releaseNote.md

mkdir -p "${INTEGRATION_DIR}"
rm  -rf "${INTEGRATION_DIR}/*"
${NODE} ./versionify.js ./Misc/InstallAgentPackage.template.xml "${INTEGRATION_DIR}/InstallAgentPackage.xml"
AGENT_VERSION_PATH=${NEW_RELEASE//./-}
mkdir -p "${INTEGRATION_DIR}/PublishVSTSAgent-${AGENT_VERSION_PATH}"
${NODE} ./versionify.js ./Misc/PublishVSTSAgent.template.ps1 "${INTEGRATION_DIR}/PublishVSTSAgent-${AGENT_VERSION_PATH}/PublishVSTSAgent-${AGENT_VERSION_PATH}.ps1"
${NODE} ./versionify.js ./Misc/UnpublishVSTSAgent.template.ps1 "${INTEGRATION_DIR}/PublishVSTSAgent-${AGENT_VERSION_PATH}/UnpublishVSTSAgent-${AGENT_VERSION_PATH}.ps1"

# make release branch
NEW_BRANCH="releases/${NEW_RELEASE}"
${GIT} checkout -b ${NEW_BRANCH}

# commit
${GIT} add ${SCRIPT_DIR}/agentversion
${GIT} add ${SCRIPT_DIR}/../releaseNote.md
${GIT} commit -m "Release ${NEW_RELEASE}"

# push
${GIT} push --set-upstream origin ${NEW_BRANCH}

# kick off release pipeline
echo "Create and publish release by kicking off this pipeline. (Use branch ${NEW_BRANCH})"
echo "     ${AGENT_RELEASE_PIPELINE_URL}"
echo
read -n 1 -p "Press any key to continue ... "
# TODO: auto kick off pipeline

# create ADO L2 tests PR
ADO_GIT_URL=https://mseng@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps
ADO_PR_URL=https://dev.azure.com/mseng/_git/AzureDevOps/pullrequests?_a=mine
${GIT} clone --depth 1 ${ADO_GIT_URL} ${INTEGRATION_DIR}/AzureDevOps
pushd ${INTEGRATION_DIR}/AzureDevOps
cp ${INTEGRATION_DIR}/InstallAgentPackage.xml DistributedTask/Service/Servicing/Host/Deployment/Groups/InstallAgentPackage.xml
NEW_ADO_BRANCH="users/${USER}/agent-${NEW_RELEASE}"
${GIT} checkout -b ${NEW_ADO_BRANCH}
${GIT} commit -a -m "Install Agent ${NEW_RELEASE}"
${GIT} push --set-upstream origin ${NEW_ADO_BRANCH}
popd
echo "Create pull-request for this change "
echo "     ${ADO_PR_URL}"
echo
read -n 1 -p "Press any key to continue ... "

# create config change PR
CONFIG_CHANGE_GIT_URL=https://mseng@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps.ConfigChange
CONFIG_CHANGE_PR_URL=https://dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps.ConfigChange/pullrequests?_a=mine

${GIT} clone --depth 1 ${CONFIG_CHANGE_GIT_URL} ${INTEGRATION_DIR}/AzureDevOps.ConfigChange
pushd ${INTEGRATION_DIR}/AzureDevOps.ConfigChange

# get most recent milestone directory
declare -a DIRS=()
for filename in ./tfs/m*; do
    base=$(basename $filename)
    DIRS+=($base)
done

MILESTONE_DIR=$(printf "%s\n" "${DIRS[@]}"  | sed -e 's/^\([a-zA-Z]*\)\([0-9]*\)\(.*\)/\1\3 \2 \1\2\3/' | sort -k 1,1 -k 2,2n | sed -e 's/^.* //' | tail -1)

cp -r "${INTEGRATION_DIR}/PublishVSTSAgent-${AGENT_VERSION_PATH}" "tfs/${MILESTONE_DIR}"
NEW_CONFIG_CHANGE_BRANCH="users/${USER}/agent-${NEW_RELEASE}"
${GIT} checkout -b ${NEW_CONFIG_CHANGE_BRANCH}
${GIT} add "tfs/${MILESTONE_DIR}"
${GIT} commit -m "Install Agent ${NEW_RELEASE}"
${GIT} push --set-upstream origin ${NEW_CONFIG_CHANGE_BRANCH}
popd
echo "Create pull-request for this change "
echo "     ${CONFIG_CHANGE_PR_URL}"
echo
read -n 1 -p "Press any key to continue ... "
# TODO: auto create PR
