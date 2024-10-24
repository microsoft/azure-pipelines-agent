#!/bin/bash

###############################################################################
#
#  ./dev.sh build/layout/test/package [Debug/Release] [optional: runtime ID]
#
###############################################################################

set -eo pipefail

ALL_ARGS=("$@")
DEV_CMD=$1
TARGET_FRAMEWORK=$2
DEV_CONFIG=$3
DEV_RUNTIME_ID=$4
DEV_TEST_FILTERS=$5
DEV_ARGS=("${ALL_ARGS[@]:5}")

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

source "$SCRIPT_DIR/.helpers.sh"

REPO_ROOT="${SCRIPT_DIR}/.."
AGENT_VERSION=$(cat "$SCRIPT_DIR/agentversion" | head -n 1 | tr -d "\n\r")

DOTNET_ERROR_PREFIX="##vso[task.logissue type=error]"
DOTNET_WARNING_PREFIX="##vso[task.logissue type=warning]"

PACKAGE_TYPE=${PACKAGE_TYPE:-agent} # agent or pipelines-agent
if [[ "$PACKAGE_TYPE" == "pipelines-agent" ]]; then
    export INCLUDE_NODE6="false"
    export INCLUDE_NODE10="false"
fi

pushd "$SCRIPT_DIR"

DEFAULT_TARGET_FRAMEWORK="net6.0"

if [[ $TARGET_FRAMEWORK == "" ]]; then
    TARGET_FRAMEWORK=$DEFAULT_TARGET_FRAMEWORK
fi

function get_net_version() {
    local dotnet_versions="
        net6.0-sdk=6.0.424
        net6.0-runtime=6.0.32

        net8.0-sdk=8.0.401
        net8.0-runtime=8.0.8
    "

    echo "$dotnet_versions" | grep -o "$1=[^ ]*" | cut -d '=' -f2
}

DOTNET_SDK_VERSION=$(get_net_version "net8.0-sdk")
DOTNET_RUNTIME_VERSION=$(get_net_version "${TARGET_FRAMEWORK}-runtime")

if [[ ($DOTNET_SDK_VERSION == "") || ($DOTNET_RUNTIME_VERSION == "") ]]; then
    failed "Incorrect target framework is specified"
fi

DOTNET_DIR="${REPO_ROOT}/_dotnetsdk"

BUILD_CONFIG="Debug"
if [[ "$DEV_CONFIG" == "Release" ]]; then
    BUILD_CONFIG="Release"
fi

restore_dotnet_install_script() {
    # run dotnet-install.ps1 on windows, dotnet-install.sh on linux
    if [[ "${CURRENT_PLATFORM}" == "windows" ]]; then
        ext="ps1"
    else
        ext="sh"
    fi

    DOTNET_INSTALL_SCRIPT_NAME="dotnet-install.${ext}"
    DOTNET_INSTALL_SCRIPT_PATH="./Misc/${DOTNET_INSTALL_SCRIPT_NAME}"

    if [[ ! -e "${DOTNET_INSTALL_SCRIPT_PATH}" ]]; then
        curl -sSL "https://dot.net/v1/${DOTNET_INSTALL_SCRIPT_NAME}" -o "${DOTNET_INSTALL_SCRIPT_PATH}"
    fi
}

function restore_sdk_and_runtime() {
    heading "Install .NET SDK ${DOTNET_SDK_VERSION} and Runtime ${DOTNET_RUNTIME_VERSION}"

    if [[ "${CURRENT_PLATFORM}" == "windows" ]]; then
        echo "Convert ${DOTNET_DIR} to Windows style path"
        local dotnet_windows_dir=${DOTNET_DIR:1}
        dotnet_windows_dir=${dotnet_windows_dir:0:1}:${dotnet_windows_dir:1}
        local architecture
        architecture=$(echo "$RUNTIME_ID" | cut -d "-" -f2)

        printf "\nInstalling SDK...\n"
        powershell -NoLogo -Sta -NoProfile -NonInteractive -ExecutionPolicy Unrestricted -Command "& \"${DOTNET_INSTALL_SCRIPT_PATH}\" -Version ${DOTNET_SDK_VERSION} -InstallDir \"${dotnet_windows_dir}\" -Architecture ${architecture}  -NoPath; exit \$LastExitCode;" || checkRC "${DOTNET_INSTALL_SCRIPT_NAME} (SDK)"

        printf "\nInstalling Runtime...\n"
        powershell -NoLogo -Sta -NoProfile -NonInteractive -ExecutionPolicy Unrestricted -Command "& \"${DOTNET_INSTALL_SCRIPT_PATH}\" -Runtime dotnet -Version ${DOTNET_RUNTIME_VERSION} -InstallDir \"${dotnet_windows_dir}\" -Architecture ${architecture} -SkipNonVersionedFiles -NoPath; exit \$LastExitCode;" || checkRC "${DOTNET_INSTALL_SCRIPT_NAME} (Runtime)"
    else
        printf "\nInstalling SDK...\n"
        bash "${DOTNET_INSTALL_SCRIPT_PATH}" --version "${DOTNET_SDK_VERSION}" --install-dir "${DOTNET_DIR}" --no-path || checkRC "${DOTNET_INSTALL_SCRIPT_NAME} (SDK)"

        printf "\nInstalling Runtime...\n"
        bash "${DOTNET_INSTALL_SCRIPT_PATH}" --runtime dotnet --version "${DOTNET_RUNTIME_VERSION}" --install-dir "${DOTNET_DIR}" --skip-non-versioned-files --no-path || checkRC "${DOTNET_INSTALL_SCRIPT_NAME} (Runtime)"
    fi
}

function detect_platform_and_runtime_id() {
    heading "Platform / RID detection"

    CURRENT_PLATFORM="windows"
    if [[ ($(uname) == "Linux") || ($(uname) == "Darwin") ]]; then
        CURRENT_PLATFORM=$(uname | awk '{print tolower($0)}')
    fi

    if [[ "$CURRENT_PLATFORM" == 'windows' ]]; then
        DETECTED_RUNTIME_ID='win-x64'
        if [[ "$PROCESSOR_ARCHITECTURE" == 'x86' ]]; then
            DETECTED_RUNTIME_ID='win-x86'
        elif [[ "$PROCESSOR_ARCHITECTURE" == 'ARM64' ]]; then
            DETECTED_RUNTIME_ID='win-arm64'
    elif [[ "$CURRENT_PLATFORM" == 'linux' ]]; then
        DETECTED_RUNTIME_ID="linux-x64"
        if command -v uname >/dev/null; then
            local CPU_NAME=$(uname -m)
            case $CPU_NAME in
            armv7l) DETECTED_RUNTIME_ID="linux-arm" ;;
            aarch64) DETECTED_RUNTIME_ID="linux-arm64" ;;
            esac
        fi

        if [ -e /etc/redhat-release ]; then
            redhatRelease=$(grep -oE "[0-9]+" /etc/redhat-release | awk "NR==1")
            if [[ "${redhatRelease}" -lt 7 ]]; then
                echo "RHEL supported for version 7 and higher."
                exit 1
            fi
        fi

        if [ -e /etc/alpine-release ]; then
            DETECTED_RUNTIME_ID='linux-musl-x64'
            if [ $(uname -m) == 'aarch64' ]; then
                DETECTED_RUNTIME_ID='linux-musl-arm64'
            fi
        fi
    elif [[ "$CURRENT_PLATFORM" == 'darwin' ]]; then
        DETECTED_RUNTIME_ID='osx-x64'
        if command -v uname >/dev/null; then
            local CPU_NAME=$(uname -m)
            case $CPU_NAME in
            arm64) DETECTED_RUNTIME_ID="osx-arm64" ;;
            esac
        fi
    fi
}

function make_build() {
    TARGET=$1

    echo "MSBuild target = ${TARGET}"

    if [[ "$ADO_ENABLE_LOGISSUE" == "true" ]]; then

        dotnet msbuild -t:"${TARGET}" -p:PackageRuntime="${RUNTIME_ID}" -p:PackageType="${PACKAGE_TYPE}" -p:BUILDCONFIG="${BUILD_CONFIG}" -p:AgentVersion="${AGENT_VERSION}" -p:LayoutRoot="${LAYOUT_DIR}" -p:CodeAnalysis="true" -p:TargetFramework="${TARGET_FRAMEWORK}" -p:RuntimeFrameworkVersion="${DOTNET_RUNTIME_VERSION}" |
            sed -e "/\: warning /s/^/${DOTNET_WARNING_PREFIX} /;" |
            sed -e "/\: error /s/^/${DOTNET_ERROR_PREFIX} /;" ||
            failed build
    else
        dotnet msbuild -t:"${TARGET}" -p:PackageRuntime="${RUNTIME_ID}" -p:PackageType="${PACKAGE_TYPE}" -p:BUILDCONFIG="${BUILD_CONFIG}" -p:AgentVersion="${AGENT_VERSION}" -p:LayoutRoot="${LAYOUT_DIR}" -p:CodeAnalysis="true" -p:TargetFramework="${TARGET_FRAMEWORK}" -p:RuntimeFrameworkVersion="${DOTNET_RUNTIME_VERSION}" ||
            failed build
    fi

    mkdir -p "${LAYOUT_DIR}/bin/en-US"

    grep -v '^ *"CLI-WIDTH-' ./Misc/layoutbin/en-US/strings.json >"${LAYOUT_DIR}/bin/en-US/strings.json"
}

function cmd_build() {
    heading "Building"

    make_build "Build"
}

function cmd_layout() {
    heading "Creating layout"

    make_build "Layout"

    #change execution flag to allow running with sudo
    if [[ ("$CURRENT_PLATFORM" == "linux") || ("$CURRENT_PLATFORM" == "darwin") ]]; then
        chmod +x "${LAYOUT_DIR}/bin/Agent.Listener"
        chmod +x "${LAYOUT_DIR}/bin/Agent.Worker"
        chmod +x "${LAYOUT_DIR}/bin/Agent.PluginHost"
        chmod +x "${LAYOUT_DIR}/bin/installdependencies.sh"
    fi

    heading "Setup externals folder for $RUNTIME_ID agent's layout"
    bash ./Misc/externals.sh $RUNTIME_ID || checkRC externals.sh
}

function cmd_test_l0() {
    heading "Testing L0"

    if [[ ("$CURRENT_PLATFORM" == "linux") || ("$CURRENT_PLATFORM" == "darwin") ]]; then
        ulimit -n 1024
    fi

    TestFilters="Level=L0&SkipOn!=${CURRENT_PLATFORM}"
    if [[ "$DEV_TEST_FILTERS" != "" ]]; then
        TestFilters="$TestFilters&$DEV_TEST_FILTERS"
    fi

    dotnet msbuild -t:testl0 -p:PackageRuntime="${RUNTIME_ID}" -p:PackageType="${PACKAGE_TYPE}" -p:BUILDCONFIG="${BUILD_CONFIG}" -p:AgentVersion="${AGENT_VERSION}" -p:LayoutRoot="${LAYOUT_DIR}" -p:TestFilters="${TestFilters}" -p:TargetFramework="${TARGET_FRAMEWORK}" -p:RuntimeFrameworkVersion="${DOTNET_RUNTIME_VERSION}" "${DEV_ARGS[@]}" || failed "failed tests"
}

function cmd_test_l1() {
    heading "Clean"

    dotnet msbuild -t:cleanl1 -p:PackageRuntime="${RUNTIME_ID}" -p:PackageType="${PACKAGE_TYPE}" -p:BUILDCONFIG="${BUILD_CONFIG}" -p:AgentVersion="${AGENT_VERSION}" -p:LayoutRoot="${LAYOUT_DIR}" || failed build

    heading "Setup externals folder for $RUNTIME_ID agent's layout"
    bash ./Misc/externals.sh $RUNTIME_ID "" "_l1" "true" || checkRC externals.sh

    heading "Testing L1"

    if [[ ("$CURRENT_PLATFORM" == "linux") || ("$CURRENT_PLATFORM" == "darwin") ]]; then
        ulimit -n 1024
    fi

    TestFilters="Level=L1&SkipOn!=${CURRENT_PLATFORM}"
    if [[ "$DEV_TEST_FILTERS" != "" ]]; then
        TestFilters="$TestFilters&$DEV_TEST_FILTERS"
    fi

    dotnet msbuild -t:testl1 -p:PackageRuntime="${RUNTIME_ID}" -p:PackageType="${PACKAGE_TYPE}" -p:BUILDCONFIG="${BUILD_CONFIG}" -p:AgentVersion="${AGENT_VERSION}" -p:LayoutRoot="${LAYOUT_DIR}" -p:TestFilters="${TestFilters}" -p:TargetFramework="${TARGET_FRAMEWORK}" -p:RuntimeFrameworkVersion="${DOTNET_RUNTIME_VERSION}" "${DEV_ARGS[@]}" || failed "failed tests"
}

function cmd_test() {
    cmd_test_l0

    cmd_test_l1
}

function cmd_package() {
    if [ ! -d "${LAYOUT_DIR}/bin" ]; then
        echo "You must build first.  Expecting to find ${LAYOUT_DIR}/bin"
    fi

    agent_ver="$AGENT_VERSION" || failed "version"

    if [[ ("$PACKAGE_TYPE" == "pipelines-agent") ]]; then
        agent_pkg_name="pipelines-agent-${RUNTIME_ID}-${agent_ver}"
    else
        agent_pkg_name="vsts-agent-${RUNTIME_ID}-${agent_ver}"
    fi

    # TEMPORARY - need to investigate why Agent.Listener --version is throwing an error on OS X
    if [ $("${LAYOUT_DIR}/bin/Agent.Listener" --version | wc -l) -gt 1 ]; then
        echo "Error thrown during --version call!"
        log_file=$("${LAYOUT_DIR}/bin/Agent.Listener" --version | head -n 2 | tail -n 1 | cut -d\  -f6)
        cat "${log_file}"
    fi
    # END TEMPORARY

    heading "Packaging ${agent_pkg_name}"

    rm -Rf "${LAYOUT_DIR:?}/_diag"

    mkdir -p "$PACKAGE_DIR"
    rm -Rf "${PACKAGE_DIR:?}"/*

    pushd "$PACKAGE_DIR" >/dev/null

    if [[ ("$CURRENT_PLATFORM" == "linux") || ("$CURRENT_PLATFORM" == "darwin") ]]; then
        tar_name="${agent_pkg_name}.tar.gz"
        echo "Creating $tar_name in ${PACKAGE_DIR}"
        tar -czf "${tar_name}" -C "${LAYOUT_DIR}" .
    elif [[ ("$CURRENT_PLATFORM" == "windows") ]]; then
        zip_name="${agent_pkg_name}.zip"
        echo "Convert ${LAYOUT_DIR} to Windows style path"
        window_path=${LAYOUT_DIR:1}
        window_path=${window_path:0:1}:${window_path:1}
        echo "Creating $zip_name in ${window_path}"
        powershell -NoLogo -Sta -NoProfile -NonInteractive -ExecutionPolicy Unrestricted -Command "Add-Type -Assembly \"System.IO.Compression.FileSystem\"; [System.IO.Compression.ZipFile]::CreateFromDirectory(\"${window_path}\", \"${zip_name}\")"
    fi

    popd >/dev/null
}

function cmd_hash() {
    pushd "$PACKAGE_DIR" >/dev/null

    files=$(ls -1)

    number_of_files=$(wc -l <<<"$files")

    if [[ number_of_files -ne 1 ]]; then
        echo "Expecting to find exactly one file (agent package) in $PACKAGE_DIR"
        exit 1
    fi

    agent_package_file=$files

    rm -rf ../../_package_hash
    mkdir ../../_package_hash
    openssl dgst -sha256 $agent_package_file >>"../../_package_hash/$agent_package_file.sha256"

    popd >/dev/null
}

function cmd_report() {
    heading "Generating Reports"

    if [[ ("$CURRENT_PLATFORM" != "windows") ]]; then
        echo "Coverage reporting only available on Windows"
        exit 1
    fi

    mkdir -p "$REPORT_DIR"

    LATEST_COVERAGE_FILE=$(find "${SCRIPT_DIR}/Test/TestResults" -type f -name '*.coverage' -print0 | xargs -r -0 ls -1 -t | head -1)

    if [[ ("$LATEST_COVERAGE_FILE" == "") ]]; then
        echo "No coverage file found. Skipping coverage report generation."
    else
        COVERAGE_REPORT_DIR=$REPORT_DIR/coverage
        mkdir -p "$COVERAGE_REPORT_DIR"
        rm -Rf "${COVERAGE_REPORT_DIR:?}"/*

        echo "Found coverage file $LATEST_COVERAGE_FILE"
        COVERAGE_XML_FILE="$COVERAGE_REPORT_DIR/coverage.xml"
        echo "Converting to XML file $COVERAGE_XML_FILE"

        # for some reason CodeCoverage.exe will only write the output file in the current directory
        pushd $COVERAGE_REPORT_DIR >/dev/null
        "${HOME}/.nuget/packages/microsoft.codecoverage/16.4.0/build/netstandard1.0/CodeCoverage/CodeCoverage.exe" analyze "/output:coverage.xml" "$LATEST_COVERAGE_FILE"
        popd >/dev/null

        if ! command -v reportgenerator.exe >/dev/null; then
            echo "reportgenerator not installed. Skipping generation of HTML reports"
            echo "To install: "
            echo "  % dotnet tool install --global dotnet-reportgenerator-globaltool"
            exit 0
        fi

        echo "Generating HTML report"
        reportgenerator.exe "-reports:$COVERAGE_XML_FILE" "-reporttypes:Html;Cobertura" "-targetdir:$COVERAGE_REPORT_DIR/coveragereport"
    fi
}

function cmd_lint() {
    heading "Linting source code"

    "${DOTNET_DIR}/dotnet" format -v diag "$REPO_ROOT/azure-pipelines-agent.sln" || checkRC "cmd_lint"
}

function cmd_lint_verify() {
    heading "Validating linted code"

    "${DOTNET_DIR}/dotnet" format --verify-no-changes -v diag "$REPO_ROOT/azure-pipelines-agent.sln" || checkRC "cmd_lint_verify"
}

detect_platform_and_runtime_id
echo "Current platform: $CURRENT_PLATFORM"
echo "Current runtime ID: $DETECTED_RUNTIME_ID"

if [ "$DEV_RUNTIME_ID" ]; then
    RUNTIME_ID=$DEV_RUNTIME_ID
else
    RUNTIME_ID=$DETECTED_RUNTIME_ID
fi

_VALID_RIDS='linux-x64:linux-arm:linux-arm64:linux-musl-x64:linux-musl-arm64:osx-x64:osx-arm64:win-x64:win-x86'
if [[ ":$_VALID_RIDS:" != *:$RUNTIME_ID:* ]]; then
    failed "must specify a valid target runtime ID (one of: $_VALID_RIDS)"
fi

echo "Building for runtime ID: $RUNTIME_ID"

LAYOUT_DIR="${REPO_ROOT}/_layout/${RUNTIME_ID}"
DOWNLOAD_DIR="${REPO_ROOT}/_downloads/${RUNTIME_ID}/netcore2x"
PACKAGE_DIR="${REPO_ROOT}/_package/${RUNTIME_ID}"
REPORT_DIR="${REPO_ROOT}/_reports/${RUNTIME_ID}"

restore_dotnet_install_script
restore_sdk_and_runtime

heading ".NET SDK to path"
echo "Adding .NET SDK to PATH (${DOTNET_DIR})"
export PATH=${DOTNET_DIR}:$PATH
echo "Path = $PATH"
echo ".NET Version = $(dotnet --version)"

heading "Pre-caching external resources for $RUNTIME_ID"
mkdir -p "${LAYOUT_DIR}" >/dev/null
bash ./Misc/externals.sh $RUNTIME_ID "Pre-Cache" || checkRC "externals.sh Pre-Cache"

if [[ "$CURRENT_PLATFORM" == 'windows' ]]; then
    vswhere=$(find "$DOWNLOAD_DIR" -name vswhere.exe | head -1)
    vs_location=$("$vswhere" -latest -property installationPath)
    msbuild_location="$vs_location""\MSBuild\15.0\Bin\msbuild.exe"

    if [[ ! -e "${msbuild_location}" ]]; then
        msbuild_location="$vs_location""\MSBuild\Current\Bin\msbuild.exe"

        if [[ ! -e "${msbuild_location}" ]]; then
            failed "Can not find msbuild location, failing build"
        fi
    fi

    export DesktopMSBuild="$msbuild_location"
fi

case $DEV_CMD in
"build") cmd_build ;;
"b") cmd_build ;;
"test") cmd_test ;;
"t") cmd_test ;;
"testl0") cmd_test_l0 ;;
"l0") cmd_test_l0 ;;
"testl1") cmd_test_l1 ;;
"l1") cmd_test_l1 ;;
"layout") cmd_layout ;;
"l") cmd_layout ;;
"package") cmd_package ;;
"p") cmd_package ;;
"hash") cmd_hash ;;
"report") cmd_report ;;
"lint") cmd_lint ;;
"lint-verify") cmd_lint_verify ;;
*) echo "Invalid command. Use (l)ayout, (b)uild, (t)est, test(l0), test(l1), or (p)ackage." ;;
esac

popd
echo
echo Done.
echo
