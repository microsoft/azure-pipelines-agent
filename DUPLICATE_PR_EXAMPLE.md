# Example: How to Duplicate a Pull Request

This document provides examples of how to use the PR duplication scripts.

## Scenario: Duplicate PR #5332

Let's say you want to duplicate PR #5332 to create a backup or for testing purposes.

### Using Bash (Linux/macOS)

```bash
# Navigate to the repository
cd /path/to/azure-pipelines-agent

# Run the duplication script
./duplicate-pullrequest.sh 5332
```

### Using PowerShell (Windows)

```powershell
# Navigate to the repository
cd C:\path\to\azure-pipelines-agent

# Run the duplication script
.\duplicate-pullrequest.ps1 -SourcePRNumber 5332
```

## What Happens

1. The script fetches PR #5332's details
2. Creates a new branch named something like: `original-branch-duplicate-20240101-120000`
3. Pushes the new branch to GitHub
4. Creates a new PR with:
   - Title: `[DUPLICATE] <original PR title>`
   - Body: References the original PR and includes the original body
   - Labels: All labels from the original PR
   - Base branch: Same as the original PR

## Output

The script will output:

```
Fetching details for PR #5332...
Original PR: #5332
Source branch: feature-branch
Target branch: master
Title: Fix job timeout
Creating duplicate branch: feature-branch-duplicate-20240101-120000
Creating duplicate PR...
Duplicate PR created successfully!
PR Number: 5333
PR URL: https://github.com/microsoft/azure-pipelines-agent/pull/5333
```

## Use Cases

1. **Testing**: Create a duplicate to test merge conflicts or CI/CD changes without affecting the original
2. **Backup**: Create a backup before making major changes to a PR
3. **Cherry-picking**: Create a duplicate targeting a different base branch
4. **Experimentation**: Test different approaches without losing the original PR

## Troubleshooting

### "PR not found"
- Verify the PR number is correct
- Ensure you have access to the repository

### "Authentication failed"
- Make sure GitHub CLI is authenticated: `gh auth login`
- Check your GitHub permissions

### "Branch already exists"
- The timestamp in the branch name should make it unique
- If the error persists, you may need to manually delete the old branch
