# PR Duplication Scripts

This directory contains scripts to duplicate GitHub Pull Requests.

## Scripts

- `duplicate-pullrequest.sh` - Bash script for Linux/macOS
- `duplicate-pullrequest.ps1` - PowerShell script for Windows

## Prerequisites

- [GitHub CLI (gh)](https://cli.github.com/) must be installed and authenticated
- `jq` command-line JSON processor (for bash script)
- Git must be configured with appropriate credentials

## Usage

### Bash (Linux/macOS)

```bash
./duplicate-pullrequest.sh <PR_NUMBER>
```

Example:
```bash
./duplicate-pullrequest.sh 123
```

### PowerShell (Windows)

```powershell
.\duplicate-pullrequest.ps1 -SourcePRNumber <PR_NUMBER>
```

Example:
```powershell
.\duplicate-pullrequest.ps1 -SourcePRNumber 123
```

## What the scripts do

1. Fetch the details of the source PR (branch names, title, body, labels)
2. Create a new branch with a unique name based on the source branch
3. Push the new branch to the remote repository
4. Create a new PR with the same content as the original, prefixed with "[DUPLICATE]"
5. Copy all labels from the original PR to the duplicate
6. Output the new PR number and URL

## Output Variables

When run in Azure Pipelines, the scripts set the following variables:

- `DUPLICATE_PR_NUMBER` - The number of the newly created duplicate PR
- `DUPLICATE_PR_LINK` - The URL of the newly created duplicate PR

## Notes

- The duplicate branch name will be: `<original-branch>-duplicate-<timestamp>`
- The duplicate PR title will be: `[DUPLICATE] <original-title>`
- The duplicate PR body will reference the original PR number
- All labels from the original PR will be copied to the duplicate
