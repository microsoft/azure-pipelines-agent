#!/bin/bash
# Example usage of the PR duplication scripts
# This file demonstrates common scenarios for duplicating PRs

echo "=== PR Duplication Examples ==="
echo ""
echo "This file contains examples of how to use the PR duplication scripts."
echo "Uncomment the example you want to run and execute this script."
echo ""

# Example 1: Basic usage - duplicate a PR
# Uncomment the line below and replace 1234 with your PR number
# ./release/duplicate-pr.sh 1234

# Example 2: Duplicate with custom branch name
# Uncomment the line below and customize as needed
# ./release/duplicate-pr.sh 1234 --branch=my-feature-branch-v2

# Example 3: Dry run to see what would happen
# Uncomment the line below to preview without making changes
# ./release/duplicate-pr.sh 1234 --dryrun

# Example 4: Check if required tools are installed
echo "Checking prerequisites..."
echo ""

# Check for git
if command -v git &> /dev/null; then
    echo "✓ Git is installed: $(git --version)"
else
    echo "✗ Git is NOT installed"
fi

# Check for gh CLI
if command -v gh &> /dev/null; then
    echo "✓ GitHub CLI is installed: $(gh --version | head -1)"
else
    echo "✗ GitHub CLI is NOT installed"
    echo "  Install from: https://cli.github.com/"
fi

# Check for node
if command -v node &> /dev/null; then
    echo "✓ Node.js is installed: $(node --version)"
else
    echo "✗ Node.js is NOT installed"
fi

# Check if authenticated with gh
echo ""
echo "Checking GitHub authentication..."
if gh auth status &> /dev/null; then
    echo "✓ Authenticated with GitHub"
else
    echo "✗ NOT authenticated with GitHub"
    echo "  Run: gh auth login"
fi

echo ""
echo "=== Examples ==="
echo ""
echo "1. Duplicate PR #1234 with auto-generated branch:"
echo "   ./release/duplicate-pr.sh 1234"
echo ""
echo "2. Duplicate PR #1234 with custom branch name:"
echo "   ./release/duplicate-pr.sh 1234 --branch=my-custom-branch"
echo ""
echo "3. Preview what would happen (dry run):"
echo "   ./release/duplicate-pr.sh 1234 --dryrun"
echo ""
echo "4. Using PowerShell (Windows):"
echo "   .\\release\\duplicate-pullrequest.ps1 -PrNumber 1234"
echo ""
echo "5. Using Node.js directly (cross-platform):"
echo "   node release/duplicatePR.js 1234 --branch=my-branch"
echo ""
