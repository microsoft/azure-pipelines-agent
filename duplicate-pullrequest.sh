#!/bin/bash
# Script to duplicate a GitHub Pull Request
# Usage: ./duplicate-pullrequest.sh <PR_NUMBER>

set -e

if [ -z "$1" ]; then
    echo "Error: PR number is required"
    echo "Usage: ./duplicate-pullrequest.sh <PR_NUMBER>"
    exit 1
fi

SOURCE_PR_NUMBER=$1

echo "Fetching details for PR #$SOURCE_PR_NUMBER..."

# Get PR details using gh CLI
PR_DATA=$(gh pr view "$SOURCE_PR_NUMBER" --json number,headRefName,baseRefName,title,body,labels)

if [ -z "$PR_DATA" ]; then
    echo "Error: PR #$SOURCE_PR_NUMBER not found"
    exit 1
fi

# Parse PR details
HEAD_REF=$(echo "$PR_DATA" | jq -r '.headRefName')
BASE_REF=$(echo "$PR_DATA" | jq -r '.baseRefName')
TITLE=$(echo "$PR_DATA" | jq -r '.title')
BODY=$(echo "$PR_DATA" | jq -r '.body')
LABELS=$(echo "$PR_DATA" | jq -r '.labels[]?.name' | tr '\n' ',' | sed 's/,$//')

echo "Original PR: #$SOURCE_PR_NUMBER"
echo "Source branch: $HEAD_REF"
echo "Target branch: $BASE_REF"
echo "Title: $TITLE"

# Generate unique branch name for the duplicate
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
DUPLICATE_BRANCH="${HEAD_REF}-duplicate-${TIMESTAMP}"

echo "Creating duplicate branch: $DUPLICATE_BRANCH"

# Fetch the source branch and create duplicate
git fetch origin "$HEAD_REF"
git checkout -b "$DUPLICATE_BRANCH" "origin/$HEAD_REF"

# Push the duplicate branch
git push -u origin "$DUPLICATE_BRANCH"

# Create the duplicate PR
DUPLICATE_TITLE="[DUPLICATE] $TITLE"
DUPLICATE_BODY="This is a duplicate of PR #${SOURCE_PR_NUMBER}

---

$BODY"

echo "Creating duplicate PR..."

# Build the gh pr create command
GH_CMD="gh pr create --head $DUPLICATE_BRANCH --base $BASE_REF --title \"$DUPLICATE_TITLE\" --body \"$DUPLICATE_BODY\""

# Add labels if they exist
if [ -n "$LABELS" ]; then
    IFS=',' read -ra LABEL_ARRAY <<< "$LABELS"
    for label in "${LABEL_ARRAY[@]}"; do
        GH_CMD="$GH_CMD --label \"$label\""
    done
fi

# Execute the command
eval "$GH_CMD"

# Wait a moment and get the new PR details
sleep 2
NEW_PR_URL=$(gh pr list --head "$DUPLICATE_BRANCH" --json url --jq '.[0].url')
NEW_PR_NUMBER=$(gh pr list --head "$DUPLICATE_BRANCH" --json number --jq '.[0].number')

if [ -n "$NEW_PR_URL" ]; then
    echo "Duplicate PR created successfully!"
    echo "PR Number: $NEW_PR_NUMBER"
    echo "PR URL: $NEW_PR_URL"
    
    # Set variables for Azure Pipelines if in pipeline context
    if [ -n "$SYSTEM_TEAMFOUNDATIONCOLLECTIONURI" ]; then
        echo "##vso[task.setvariable variable=DUPLICATE_PR_NUMBER]$NEW_PR_NUMBER"
        echo "##vso[task.setvariable variable=DUPLICATE_PR_LINK]$NEW_PR_URL"
    fi
else
    echo "Warning: Duplicate PR may have been created, but unable to retrieve details."
fi
