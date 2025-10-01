# Pull Request Duplication Guide

## Overview

The Azure Pipelines Agent repository includes tools to duplicate existing GitHub Pull Requests. This can be useful when you need to recreate a PR that was closed, create a backup of changes, or test changes on a different branch.

## Quick Start

### For Linux/macOS Users

```bash
# Duplicate PR #1234
./release/duplicate-pr.sh 1234

# With custom branch name
./release/duplicate-pr.sh 1234 --branch=my-custom-branch

# Dry run to preview what would happen
./release/duplicate-pr.sh 1234 --dryrun
```

### For Windows Users (PowerShell)

```powershell
# Duplicate PR #1234
.\release\duplicate-pullrequest.ps1 -PrNumber 1234

# With custom branch name
.\release\duplicate-pullrequest.ps1 -PrNumber 1234 -NewBranchName "my-custom-branch"

# Dry run to preview
.\release\duplicate-pullrequest.ps1 -PrNumber 1234 -DryRun
```

### For Cross-Platform (Node.js)

```bash
# Works on all platforms
node release/duplicatePR.js 1234

# With options
node release/duplicatePR.js 1234 --branch=my-branch --dryrun
```

## Prerequisites

Before using these scripts, ensure you have:

1. **Git** - Version control system
2. **GitHub CLI (gh)** - Install from https://cli.github.com/
3. **Node.js** (for Node.js version) - Version 12 or later
4. **npm dependencies** (for Node.js version) - Run `npm install` in the `release/` directory

### Installing GitHub CLI

#### macOS
```bash
brew install gh
```

#### Windows
```powershell
winget install --id GitHub.cli
# or
choco install gh
```

#### Linux
```bash
# Debian/Ubuntu
sudo apt install gh

# Fedora/CentOS/RHEL
sudo dnf install gh

# Arch
sudo pacman -S github-cli
```

### Authenticating with GitHub

After installing the GitHub CLI, authenticate:

```bash
gh auth login
```

Follow the prompts to complete authentication.

## How It Works

The scripts perform the following steps:

1. **Fetch PR Details**: Retrieves the original PR's metadata (title, body, base branch, state)
2. **Get Diff**: Downloads all file changes from the PR
3. **Checkout Base Branch**: Switches to the base branch and pulls latest changes
4. **Create New Branch**: Creates a new branch from the updated base
5. **Apply Changes**: Applies all file changes from the original PR
6. **Commit**: Creates a commit with the same title as the original PR
7. **Push**: Pushes the new branch to the remote repository
8. **Create PR**: Opens a new PR with the same title and a modified body that references the original

## Common Use Cases

### Recreating a Closed PR

If a PR was accidentally closed or needs to be reopened with a fresh branch:

```bash
./release/duplicate-pr.sh 1234 --branch=recreate-feature-x
```

### Creating a Backup Before Major Changes

Before making significant modifications to an existing PR:

```bash
./release/duplicate-pr.sh 1234 --branch=backup-pr-1234
```

### Testing on a Different Branch

To test how changes work with the latest code:

```bash
# This creates a new PR from the current state of the base branch
./release/duplicate-pr.sh 1234 --branch=test-with-latest
```

### Dry Run Mode

To see what would happen without making any changes:

```bash
./release/duplicate-pr.sh 1234 --dryrun
```

This will show:
- Branch name that would be created
- Number of lines in the patch
- Commit message
- PR title and body

## Troubleshooting

### Error: "gh: command not found"

**Solution**: Install the GitHub CLI as described in the Prerequisites section.

### Error: "gh: not authenticated"

**Solution**: Run `gh auth login` to authenticate with GitHub.

### Error: "Failed to apply patch"

This can happen if:
- The base branch has diverged significantly
- There are conflicts with the current state of the base branch
- Binary files or special file types are involved

**Solution**: 
1. Try using dry run mode to see the changes: `--dryrun`
2. Manually review the original PR at: `https://github.com/microsoft/azure-pipelines-agent/pull/<number>`
3. Consider manually recreating the changes instead of using the script

### Error: "You have uncommitted changes"

**Solution**: Commit or stash your current changes before running the script:
```bash
git stash
./release/duplicate-pr.sh 1234
git stash pop
```

### Error: "Branch already exists"

**Solution**: Choose a different branch name:
```bash
./release/duplicate-pr.sh 1234 --branch=duplicate-pr-1234-v2
```

## Advanced Usage

### Installing Node.js Dependencies

If you're using the Node.js version directly, install dependencies first:

```bash
cd release
npm install
cd ..
node release/duplicatePR.js 1234
```

### Custom PR Body

The duplicated PR will automatically include a reference to the original PR:

```
This PR is a duplicate of #1234

---

[Original PR body content]
```

### Checking PR Status

Before duplicating, you can check the PR status:

```bash
gh pr view 1234
```

## Additional Resources

- [GitHub CLI Documentation](https://cli.github.com/manual/)
- [Git Documentation](https://git-scm.com/doc)
- [Full Script Documentation](../release/README-DUPLICATE-PR.md)

## Contributing

If you encounter issues with these scripts or have suggestions for improvements, please open an issue or submit a PR to the [Azure Pipelines Agent repository](https://github.com/microsoft/azure-pipelines-agent).
