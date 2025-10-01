#!/bin/bash
# Wrapper script to duplicate a GitHub Pull Request
# This is a convenience wrapper around the Node.js duplicatePR.js script

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
NODE_SCRIPT="$SCRIPT_DIR/duplicatePR.js"

# Check if node is available
if ! command -v node &> /dev/null; then
    echo "Error: Node.js is not installed or not in PATH"
    echo "Please install Node.js to use this script"
    exit 1
fi

# Check if gh CLI is available
if ! command -v gh &> /dev/null; then
    echo "Error: GitHub CLI (gh) is not installed or not in PATH"
    echo "Please install GitHub CLI: https://cli.github.com/"
    exit 1
fi

# Run the Node.js script with all arguments passed through
node "$NODE_SCRIPT" "$@"
