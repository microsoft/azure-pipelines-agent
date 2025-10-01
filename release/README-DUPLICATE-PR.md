# PR Duplication Scripts

This directory contains scripts to duplicate existing GitHub Pull Requests with the same title, body, and file changes.

## Overview

These scripts allow you to create a copy of an existing PR, which is useful when you need to:
- Recreate a PR that was closed accidentally
- Create a new PR with the same changes on a different branch
- Duplicate changes for testing or backup purposes

## Available Scripts

### Bash Wrapper: `duplicate-pr.sh` (Linux/macOS)

A convenient bash wrapper script for Unix-like systems.

#### Prerequisites
- Bash
- Node.js (v12 or later)
- Git
- GitHub CLI (`gh`) installed and authenticated

#### Usage

```bash
./release/duplicate-pr.sh <pr-number> [options]
```

#### Examples

```bash
# Duplicate PR #1234
./release/duplicate-pr.sh 1234

# Duplicate with custom branch
./release/duplicate-pr.sh 1234 --branch=my-branch

# Dry run
./release/duplicate-pr.sh 1234 --dryrun
```

### Node.js Version: `duplicatePR.js`

A cross-platform Node.js script that works on Windows, macOS, and Linux.

#### Prerequisites
- Node.js (v12 or later)
- Git
- GitHub CLI (`gh`) installed and authenticated

#### Usage

```bash
node release/duplicatePR.js <pr-number> [options]
```

#### Options
- `--branch=<name>`: Name for the new branch (default: `duplicate-pr-{number}`)
- `--dryrun`: Perform a dry run without actually creating the PR
- `-h, --help`: Display help information

#### Examples

```bash
# Duplicate PR #1234 with auto-generated branch name
node release/duplicatePR.js 1234

# Duplicate PR with custom branch name
node release/duplicatePR.js 1234 --branch=my-custom-branch

# Dry run to see what would happen
node release/duplicatePR.js 1234 --dryrun
```

### PowerShell Version: `duplicate-pullrequest.ps1`

A PowerShell script optimized for Windows environments.

#### Prerequisites
- PowerShell 5.1 or later (PowerShell Core 6+ recommended)
- Git
- GitHub CLI (`gh`) installed and authenticated

#### Usage

```powershell
.\release\duplicate-pullrequest.ps1 -PrNumber <number> [-NewBranchName <name>] [-DryRun]
```

#### Parameters
- `-PrNumber`: **(Required)** The number of the PR to duplicate
- `-NewBranchName`: (Optional) Name for the new branch (default: `duplicate-pr-{number}`)
- `-DryRun`: (Optional) Perform a dry run without actually creating the PR

#### Examples

```powershell
# Duplicate PR #1234 with auto-generated branch name
.\release\duplicate-pullrequest.ps1 -PrNumber 1234

# Duplicate PR with custom branch name
.\release\duplicate-pullrequest.ps1 -PrNumber 1234 -NewBranchName "my-custom-branch"

# Dry run to see what would happen
.\release\duplicate-pullrequest.ps1 -PrNumber 1234 -DryRun
```

## How It Works

Both scripts follow the same workflow:

1. **Fetch PR Details**: Uses GitHub CLI to retrieve the PR's title, body, base branch, and state
2. **Get Diff**: Fetches the complete diff of all changes in the PR
3. **Create Branch**: Creates a new branch from the base branch
4. **Apply Changes**: Applies the diff as a patch to the new branch
5. **Commit**: Commits the changes with the same title as the original PR
6. **Push**: Pushes the new branch to the remote repository
7. **Create PR**: Creates a new PR with the same title and a modified body that references the original PR

## Notes

- The new PR's body will include a reference to the original PR number
- The script will fail if you have uncommitted changes in your working directory
- The base branch will be checked out and updated before creating the new branch
- The new PR will be created against the same base branch as the original PR

## Troubleshooting

### "gh: command not found"
Install the GitHub CLI:
- **macOS**: `brew install gh`
- **Windows**: `winget install --id GitHub.cli`
- **Linux**: See [GitHub CLI installation guide](https://github.com/cli/cli#installation)

### "gh: not authenticated"
Authenticate with GitHub:
```bash
gh auth login
```

### "Failed to apply patch"
This can happen if:
- The base branch has diverged significantly from when the original PR was created
- There are conflicts with the current state of the base branch
- The diff format is not compatible

Try manually reviewing the original PR and recreating the changes if the automatic patch fails.

## Examples of Use Cases

### Recreating a Closed PR
If a PR was accidentally closed or merged to the wrong branch:
```bash
node release/duplicatePR.js 1234 --branch=recreate-feature-x
```

### Creating a Backup PR
Before making major changes to an existing PR:
```bash
node release/duplicatePR.js 1234 --branch=backup-pr-1234
```

### Testing Changes
To test how changes would work on a different base branch, you can duplicate the PR and then manually change the base branch in the GitHub UI.
