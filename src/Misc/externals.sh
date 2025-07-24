#!/bin/bash
PACKAGERUNTIME=$1
PRECACHE=$2
LAYOUT_DIR=$3
L1_MODE=$4

INCLUDE_NODE6=${INCLUDE_NODE6:-true}
INCLUDE_NODE10=${INCLUDE_NODE10:-true}

CONTAINER_URL=https://vstsagenttools.blob.core.windows.net/tools

NODE_URL=https://nodejs.org/dist
NODE_UNOFFICIAL_URL=https://unofficial-builds.nodejs.org/download/release

if [[ "$PACKAGERUNTIME" == "linux-musl-x64" ]]; then
    NODE_URL=https://unofficial-builds.nodejs.org/download/release
    INCLUDE_NODE6=false
fi

if [[ "$PACKAGERUNTIME" == "win-arm64" ]]; then
    INCLUDE_NODE6=false
    INCLUDE_NODE10=false;
fi

NODE_VERSION="6.17.1"
NODE10_VERSION="10.24.1"
NODE16_VERSION="16.20.2"
NODE16_WIN_ARM64_VERSION="16.9.1"
NODE20_VERSION="20.19.4"
MINGIT_VERSION="2.49.0"
LFS_VERSION="3.4.0"

get_abs_path() {
  # exploits the fact that pwd will print abs path when no args
  echo "$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
}

DOWNLOAD_DIR="$(get_abs_path "$(dirname $0)/../../_downloads")/$PACKAGERUNTIME/netcore2x"
if [[ "$LAYOUT_DIR" == "" ]]; then
    LAYOUT_DIR=$(get_abs_path "$(dirname $0)/../../_layout/$PACKAGERUNTIME")
else
    LAYOUT_DIR=$(get_abs_path "$(dirname $0)/../../$LAYOUT_DIR")
fi

function failed() {
    local error=${1:-Undefined error}
    local command_identifier=$2
    echo "Failed: $error" >&2
    if [[ $command_identifier == 'download_node_alpine_arm64' ]]; then
        echo "Node for Alpine ARM64 not found in blob storage. If the version of Node (for tasks execution) has been updated, it should be built for Alpine ARM64 and uploaded to blob storage. Read documentation about the agent release for more info."
    fi
    exit 1
}

function checkRC() {
    local rc=$?
    local command_identifier=$2
    if [ $rc -ne 0 ]; then
        failed "${1} failed with return code $rc" $command_identifier
    fi
}

function acquireExternalTool() {
    local download_source=$1 # E.g. https://vstsagenttools.blob.core.windows.net/tools/pdbstr/1/pdbstr.zip
    local target_dir="$LAYOUT_DIR/externals/$2" # E.g. $LAYOUT_DIR/externals/pdbstr
    local fix_nested_dir=$3 # Flag that indicates whether to move nested contents up one directory. E.g. TEE-CLC-14.0.4.zip
                            # directly contains only a nested directory TEE-CLC-14.0.4. When this flag is set, the contents
                            # of the nested TEE-CLC-14.0.4 directory are moved up one directory, and then the empty directory
                            # TEE-CLC-14.0.4 is removed.
    local dont_uncompress=$4
    local tool_name=$5

    # Extract the portion of the URL after the protocol. E.g. vstsagenttools.blob.core.windows.net/tools/pdbstr/1/pdbstr.zip
    local relative_url="${download_source#*://}"

    # Check if the download already exists.
    local download_target="$DOWNLOAD_DIR/$relative_url"
    local download_basename="$(basename "$download_target")"
    local download_dir="$(dirname "$download_target")"

    if [[ "$PRECACHE" != "" ]]; then
        if [ -f "$download_target" ]; then
            echo "Download exists: $download_basename"
        else
            # Delete any previous partial file.
            local partial_target="$DOWNLOAD_DIR/partial/$download_basename"
            mkdir -p "$(dirname "$partial_target")" || checkRC 'mkdir'
            if [ -f "$partial_target" ]; then
                rm "$partial_target" || checkRC 'rm'
            fi

            # Download from source to the partial file.
            echo "Downloading $download_source"
            mkdir -p "$(dirname "$download_target")" || checkRC 'mkdir'
            # curl -f Fail silently (no output at all) on HTTP errors (H)
            #      -k Allow connections to SSL sites without certs (H)
            #      -S Show error. With -s, make curl show errors when they occur
            #      -L Follow redirects (H)
            #      -o FILE    Write to FILE instead of stdout
            curl --retry 10 -fkSL -o "$partial_target" "$download_source" 2>"${download_target}_download.log" || checkRC 'curl' "download_${tool_name}"

            # Move the partial file to the download target.
            mv "$partial_target" "$download_target" || checkRC 'mv'

            # Extract to current directory
            # Ensure we can extract those files
            # We might use them during dev.sh
            local extract_dir="$download_dir/$download_basename.extract"
            mkdir -p "$extract_dir" || checkRC 'mkdir'
            if [[ "$download_basename" == *.zip ]]; then
                # Extract the zip.
                echo "Testing zip"
                unzip "$download_target" -d "$extract_dir" > /dev/null
                local rc=$?
                if [[ $rc -ne 0 && $rc -ne 1 ]]; then
                    failed "unzip failed with return code $rc"
                fi
            elif [[ "$download_basename" == *.tar.gz ]]; then
                # Extract the tar gz.
                echo "Testing tar gz"
                tar xzf "$download_target" -C "$extract_dir" > /dev/null || checkRC 'tar'
            fi
        fi
    else
        # Extract to layout.
        mkdir -p "$target_dir" || checkRC 'mkdir'
        local nested_dir=""
        if [[ "$download_basename" == *.zip && "$dont_uncompress" != "dont_uncompress" ]]; then
            # Extract the zip.
            echo "Extracting zip from $download_target to $target_dir"
            unzip "$download_target" -d "$target_dir" > /dev/null
            local rc=$?
            if [[ $rc -ne 0 && $rc -ne 1 ]]; then
                failed "unzip failed with return code $rc"
            fi

            # Capture the nested directory path if the fix_nested_dir flag is set.
            if [[ "$fix_nested_dir" == "fix_nested_dir" ]]; then
                nested_dir="${download_basename%.zip}" # Remove the trailing ".zip".
            fi
        elif [[ "$download_basename" == *.tar.gz && "$dont_uncompress" != "dont_uncompress" ]]; then
            # Extract the tar gz.
            echo "Extracting tar gz from $download_target to $target_dir"
            tar xzf "$download_target" -C "$target_dir" > /dev/null || checkRC 'tar'

            # Capture the nested directory path if the fix_nested_dir flag is set.
            if [[ "$fix_nested_dir" == "fix_nested_dir" ]]; then
                nested_dir="${download_basename%.tar.gz}" # Remove the trailing ".tar.gz".
            fi
        else
            # Copy the file.
            echo "Copying from $download_target to $target_dir"
            cp "$download_target" "$target_dir/" || checkRC 'cp'
        fi

        # Fixup the nested directory.
        if [[ "$nested_dir" != "" ]]; then
            if [ -d "$target_dir/$nested_dir" ]; then
                mv "$target_dir/$nested_dir"/* "$target_dir/" || checkRC 'mv'
                rmdir "$target_dir/$nested_dir" || checkRC 'rmdir'
            fi
        fi
    fi
}

echo "PACKAGE RUNTIME: $PACKAGERUNTIME"

if [[ "$PACKAGERUNTIME" == "win-x"* ]]; then
    # Download external tools for Windows.

    BIT="32"
    if [[ "$PACKAGERUNTIME" == "win-x64" ]]; then
        BIT="64"
        acquireExternalTool "$CONTAINER_URL/azcopy/1/azcopy.zip" azcopy
        acquireExternalTool "$CONTAINER_URL/vstshost/m122_887c6659_binding_redirect_patched/vstshost.zip" vstshost
        acquireExternalTool "$CONTAINER_URL/vstsom/m153_47c0856d_adhoc/vstsom.zip" vstsom
    fi

    acquireExternalTool "$CONTAINER_URL/mingit/${MINGIT_VERSION}/MinGit-${MINGIT_VERSION}-${BIT}-bit.zip" git
    acquireExternalTool "$CONTAINER_URL/git-lfs/${LFS_VERSION}/x${BIT}/git-lfs.exe" "git/mingw${BIT}/bin"
    acquireExternalTool "$CONTAINER_URL/pdbstr/1/pdbstr.zip" pdbstr
    acquireExternalTool "$CONTAINER_URL/symstore/1/symstore.zip" symstore
    acquireExternalTool "$CONTAINER_URL/vstsom/m153_47c0856d_adhoc/vstsom.zip" tf
    acquireExternalTool "$CONTAINER_URL/vswhere/2_8_4/vswhere.zip" vswhere
    acquireExternalTool "https://dist.nuget.org/win-x86-commandline/v4.6.4/nuget.exe" nuget

    if [[ "$INCLUDE_NODE6" == "true" ]]; then
        acquireExternalTool "${NODE_URL}/v${NODE_VERSION}/${PACKAGERUNTIME}/node.exe" node/bin
        acquireExternalTool "${NODE_URL}/v${NODE_VERSION}/${PACKAGERUNTIME}/node.lib" node/bin
    fi
    if [[ "$INCLUDE_NODE10" == "true" ]]; then
        acquireExternalTool "${NODE_URL}/v${NODE10_VERSION}/${PACKAGERUNTIME}/node.exe" node10/bin
        acquireExternalTool "${NODE_URL}/v${NODE10_VERSION}/${PACKAGERUNTIME}/node.lib" node10/bin
    fi
    acquireExternalTool "${NODE_URL}/v${NODE16_VERSION}/${PACKAGERUNTIME}/node.exe" node16/bin
    acquireExternalTool "${NODE_URL}/v${NODE16_VERSION}/${PACKAGERUNTIME}/node.lib" node16/bin
    acquireExternalTool "${NODE_URL}/v${NODE20_VERSION}/${PACKAGERUNTIME}/node.exe" node20_1/bin
    acquireExternalTool "${NODE_URL}/v${NODE20_VERSION}/${PACKAGERUNTIME}/node.lib" node20_1/bin
elif [[ "$PACKAGERUNTIME" == "win-arm64" || "$PACKAGERUNTIME" == "win-arm32" ]]; then
    # Download external tools for Windows ARM

    BIT="32"
    if [[ "$PACKAGERUNTIME" == "win-arm64" ]]; then
        BIT="64"

        # acquireExternalTool "$CONTAINER_URL/azcopy/1/azcopy.zip" azcopy # Unavailable for Win ARM 64 - https://learn.microsoft.com/en-us/azure/storage/common/storage-use-azcopy-v10?tabs=dnf#download-the-azcopy-portable-binary
        acquireExternalTool "$CONTAINER_URL/vstshost/m122_887c6659_binding_redirect_patched/vstshost.zip" vstshost  # Custom package. Will the same work for Win ARM 64?
        acquireExternalTool "$CONTAINER_URL/vstsom/m153_47c0856d_adhoc/vstsom.zip" vstsom  # Custom package. Will the same work for Win ARM 64?
    fi

    acquireExternalTool "$CONTAINER_URL/mingit/${MINGIT_VERSION}/MinGit-${MINGIT_VERSION}-${BIT}-bit.zip" git # Unavailable for Win ARM 64 - https://github.com/git-for-windows/git/releases
    acquireExternalTool "$CONTAINER_URL/git-lfs/${LFS_VERSION}/win-arm${BIT}/git-lfs.exe" "git/mingw${BIT}/bin"
    acquireExternalTool "$CONTAINER_URL/pdbstr/win-arm${BIT}/1/pdbstr.zip" pdbstr
    acquireExternalTool "$CONTAINER_URL/symstore/win-arm${BIT}/1/symstore.zip" symstore
    acquireExternalTool "$CONTAINER_URL/vstsom/m153_47c0856d_adhoc/vstsom.zip" tf
    acquireExternalTool "$CONTAINER_URL/vswhere/2_8_4/vswhere.zip" vswhere
    acquireExternalTool "https://dist.nuget.org/win-x86-commandline/v4.6.4/nuget.exe" nuget

    if [[ "$INCLUDE_NODE6" == "true" ]]; then
        acquireExternalTool "${NODE_URL}/v${NODE_VERSION}/${PACKAGERUNTIME}/node.exe" node/bin # Not available for Windows ARM
        acquireExternalTool "${NODE_URL}/v${NODE_VERSION}/${PACKAGERUNTIME}/node.lib" node/bin # Not available for Windows ARM
    fi
    if [[ "$INCLUDE_NODE10" == "true" ]]; then
        acquireExternalTool "${NODE_URL}/v${NODE10_VERSION}/${PACKAGERUNTIME}/node.exe" node10/bin # Not available for Windows ARM
        acquireExternalTool "${NODE_URL}/v${NODE10_VERSION}/${PACKAGERUNTIME}/node.lib" node10/bin # Not available for Windows ARM
    fi

    # Unofficial distribution of Node contains Node 16 for Windows ARM
    acquireExternalTool "${NODE_UNOFFICIAL_URL}/v${NODE16_WIN_ARM64_VERSION}/${PACKAGERUNTIME}/node.exe" node16/bin
    acquireExternalTool "${NODE_UNOFFICIAL_URL}/v${NODE16_WIN_ARM64_VERSION}/${PACKAGERUNTIME}/node.lib" node16/bin

    # Official distribution of Node contains Node 20 for Windows ARM
    acquireExternalTool "${NODE_URL}/v${NODE20_VERSION}/${PACKAGERUNTIME}/node.exe" node20_1/bin
    acquireExternalTool "${NODE_URL}/v${NODE20_VERSION}/${PACKAGERUNTIME}/node.lib" node20_1/bin
else
    # Download external tools for Linux and OSX.

    if [[ "$PACKAGERUNTIME" == "osx-arm64" ]]; then
        ARCH="darwin-x64"
        if [[ "$INCLUDE_NODE6" == "true" ]]; then
            acquireExternalTool "${NODE_URL}/v${NODE_VERSION}/node-v${NODE_VERSION}-${ARCH}.tar.gz" node fix_nested_dir
        fi

        if [[ "$INCLUDE_NODE10" == "true" ]]; then
            acquireExternalTool "${NODE_URL}/v${NODE10_VERSION}/node-v${NODE10_VERSION}-${ARCH}.tar.gz" node10 fix_nested_dir
        fi

        ARCH="darwin-arm64"
        acquireExternalTool "${NODE_URL}/v${NODE16_VERSION}/node-v${NODE16_VERSION}-${ARCH}.tar.gz" node16 fix_nested_dir
        acquireExternalTool "${NODE_URL}/v${NODE20_VERSION}/node-v${NODE20_VERSION}-${ARCH}.tar.gz" node20_1 fix_nested_dir
    elif [[ "$PACKAGERUNTIME" == "linux-musl-arm64" ]]; then
        ARCH="linux-arm64-musl"

        if [[ "$INCLUDE_NODE10" == "true" ]]; then
            acquireExternalTool "${CONTAINER_URL}/nodejs/${ARCH}/node-v${NODE10_VERSION}-${ARCH}.tar.gz" node10/bin fix_nested_dir false node_alpine_arm64
        fi

        acquireExternalTool "${CONTAINER_URL}/nodejs/${ARCH}/node-v${NODE16_VERSION}-${ARCH}.tar.gz" node16/bin fix_nested_dir false node_alpine_arm64
        acquireExternalTool "${CONTAINER_URL}/nodejs/${ARCH}/node-v${NODE20_VERSION}-${ARCH}.tar.gz" node20_1/bin fix_nested_dir false node_alpine_arm64
    else
        case $PACKAGERUNTIME in
            "linux-musl-x64") ARCH="linux-x64-musl";;
            "linux-x64") ARCH="linux-x64";;
            "linux-arm") ARCH="linux-armv7l";;
            "linux-arm64") ARCH="linux-arm64";;
            "osx-x64") ARCH="darwin-x64";;
            *) echo "Unknown PACKAGERUNTIME: ${PACKAGERUNTIME}"; exit 1;;
        esac

        if [[ "$INCLUDE_NODE6" == "true" ]]; then
            acquireExternalTool "${NODE_URL}/v${NODE_VERSION}/node-v${NODE_VERSION}-${ARCH}.tar.gz" node fix_nested_dir
        fi
        if [[ "$INCLUDE_NODE10" == "true" ]]; then
            acquireExternalTool "${NODE_URL}/v${NODE10_VERSION}/node-v${NODE10_VERSION}-${ARCH}.tar.gz" node10 fix_nested_dir
        fi
        acquireExternalTool "${NODE_URL}/v${NODE16_VERSION}/node-v${NODE16_VERSION}-${ARCH}.tar.gz" node16 fix_nested_dir
        acquireExternalTool "${NODE_URL}/v${NODE20_VERSION}/node-v${NODE20_VERSION}-${ARCH}.tar.gz" node20_1 fix_nested_dir
    fi
    # remove `npm`, `npx`, `corepack`, and related `node_modules` from the `externals/node*` agent directory
    # they are installed along with node, but agent does not use them

    rm -rf "$LAYOUT_DIR/externals/node/lib"
    rm "$LAYOUT_DIR/externals/node/bin/npm"

    rm -rf "$LAYOUT_DIR/externals/node10/lib"
    rm "$LAYOUT_DIR/externals/node10/bin/npm"
    rm "$LAYOUT_DIR/externals/node10/bin/npx"

    rm -rf "$LAYOUT_DIR/externals/node16/lib"
    rm "$LAYOUT_DIR/externals/node16/bin/npm"
    rm "$LAYOUT_DIR/externals/node16/bin/npx"
    rm "$LAYOUT_DIR/externals/node16/bin/corepack"

    rm -rf "$LAYOUT_DIR/externals/node20_1/lib"
    rm "$LAYOUT_DIR/externals/node20_1/bin/npm"
    rm "$LAYOUT_DIR/externals/node20_1/bin/npx"
    rm "$LAYOUT_DIR/externals/node20_1/bin/corepack"
fi

if [[ "$L1_MODE" != "" || "$PRECACHE" != "" ]]; then
    # cmdline task
    acquireExternalTool "$CONTAINER_URL/l1Tasks/d9bafed4-0b18-4f58-968d-86655b4d2ce9.zip" "Tasks" false dont_uncompress
    # cmdline node10 task
    acquireExternalTool "$CONTAINER_URL/l1Tasks/f9bafed4-0b18-4f58-968d-86655b4d2ce9.zip" "Tasks" false dont_uncompress

    # with the current setup of this package there are backslashes so it fails to extract on non-windows at runtime
    # we may need to fix this in the Agent
    if [[ "$PACKAGERUNTIME" == "win-x"* ]]; then
        # signed service tree task
        acquireExternalTool "$CONTAINER_URL/l1Tasks/d9bafed4-0b18-4f58-0001-86655b4d2ce9.zip" "Tasks" false dont_uncompress
    fi
fi
