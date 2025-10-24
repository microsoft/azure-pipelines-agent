# Azure Pipelines Agent Release Scripts

This directory contains the release automation scripts for the Azure Pipelines Agent. These scripts are used in the Azure DevOps release pipelines to automate various aspects of the release process.

## Scripts Overview

| Script | Purpose | Dependencies Used |
|--------|---------|-------------------|
| `createReleaseBranch.js` | Creates release branches and generates release notes from PRs | `@octokit/rest`, `@octokit/graphql`, `azure-devops-node-api` |
| `createAdoPrs.js` | Creates Azure DevOps pull requests for integration files | `azure-devops-node-api`, `azure-pipelines-task-lib` |
| `fillReleaseNotesTemplate.js` | Fills release notes template with version and hash values | `util.js` (local) |
| `rollrelease.js` | Manages GitHub releases (marks as non-prerelease) | `@octokit/rest` |
| `util.js` | Utility functions for file operations and git commands | Node.js built-ins |

## Testing Scripts After Dependency Updates

When updating npm dependencies in `package.json`, follow these steps to ensure all scripts continue working:

### 1. Required Environment Setup

#### Node.js and npm Version Requirements

The release pipeline uses **Node.js 20.19.4** as specified in `.vsts.release.yml`. Ensure you're using this version for consistency:
Please double check the version of node in `.vsts.release.yml` as the version mentioned above might have changed there. 

```bash
# Check your current Node.js version
node --version

# If using nvm to manage Node.js versions:
nvm use 20.19.4
# or install if not available:
nvm install 20.19.4

# Verify npm version (should be compatible with Node.js 20.19.4)
npm --version
```
### 2. Update Dependencies

```bash
cd release/
npm update
# or for major version updates:
npm install package@latest
npm audit fix --force
```

### 3. Test Each Script

#### A. Test `fillReleaseNotesTemplate.js`

```bash
# Create mock hash files for testing (required - scripts expect these to exist)
# Note: In production builds, these are generated automatically by the build process
mkdir -p ../_hashes/hash
echo "abcd1234567890abcd1234567890abcd1234567890abcd1234567890abcd1234" > ../_hashes/hash/vsts-agent-win-x64-3.999.999.zip.sha256
echo "efgh1234567890efgh1234567890efgh1234567890efgh1234567890efgh1234" > ../_hashes/hash/vsts-agent-osx-x64-3.999.999.tar.gz.sha256

# Test the script
node fillReleaseNotesTemplate.js 3.999.999

# Check if releaseNote.md was modified correctly
git diff ../releaseNote.md

# Restore original file
git restore ../releaseNote.md
```

#### B. Test `rollrelease.js`

```bash
# Test with dry-run and a real release version (requires GitHub PAT)
PAT="your_github_pat" node rollrelease.js --dryrun --stage="Ring 5" --ghpat="${PAT}" 3.246.0

# Expected: Should connect to GitHub and show what it would do without errors
```

#### C. Test `createReleaseBranch.js`

```bash
# Test with dry-run mode (requires GitHub PAT)
PAT="your_github_pat" node createReleaseBranch.js 4.262.0 --derivedFrom=lastMinorRelease --targetCommitId=$(git rev-parse HEAD) --dryrun

# Expected: Should execute git operations but skip the push step
# Look for: "Dry run mode: skipping push" message
```

#### D. Test `createAdoPrs.js`

```bash
# Test with dry-run mode (requires Azure DevOps PAT)
PAT="your_azdo_pat" node createAdoPrs.js --dryrun=true 3.999.999

# Expected: Should create integration files and show "Dry run: Skipping Azure DevOps API calls"
# Should NOT show authentication errors (401)
```

#### For Testing with Real APIs

1. **GitHub PAT**: Required for `rollrelease.js` and `createReleaseBranch.js`
   - Set `PAT` environment variable
   - Needs `repo` scope permissions

2. **Azure DevOps PAT**: Required for `createAdoPrs.js`
   - Set `PAT` environment variable  
   - Needs `Code (read & write)` and `Pull Request (read & write)` permissions

3. **Git Configuration**: Required for all scripts that make commits
   ```bash
   git config --global user.email "your.email@domain.com"
   git config --global user.name "Your Name"
   ```

#### Mock Data Setup

Some scripts expect certain directories/files to exist:

```bash
# For hash-related scripts (REQUIRED - scripts will fail without these)
mkdir -p ../_hashes/hash
# Create mock hash files for testing as shown in fillReleaseNotesTemplate.js section

# For integration file generation
mkdir -p ../_layout/integrations
```


### 4. Common Issues and Solutions

#### Package Compatibility Issues

**Symptom**: `TypeError: got.get is not a function` or similar method errors
**Solution**: Check if the package changed its API. Update the code to use the new API.

**Example**: `got` library changed from `got.get()` to `got()` in v12+
```javascript
// Old (v11 and earlier)
const response = await got.get(url, options);

// New (v12+) 
const response = await got(url, options);
```

#### Missing Dependencies

**Symptom**: `Cannot find module 'package-name'`
**Solution**: Ensure the package is listed in `package.json` and run `npm install`

#### Authentication Issues

**Symptom**: `401 Unauthorized` errors during dry-run
**Solution**: Ensure API calls are properly wrapped in dry-run checks:
```javascript
if (dryrun) {
    console.log('Dry run: Skipping API calls');
    return mockResponse;
}
// Make actual API calls here
```

### 5. Validation Checklist

After testing all scripts, verify:

- [ ] All scripts run without syntax errors
- [ ] Dry-run modes work correctly (no unintended API calls)
- [ ] Scripts handle missing files/directories gracefully
- [ ] Updated packages don't introduce security vulnerabilities (`npm audit`)
- [ ] Git operations execute properly in dry-run mode
- [ ] API authentication works with provided PATs
- [ ] Generated files (integration files, release notes) are correct

### 6. Pipeline Integration

These scripts are used in `.vsts.release.yml`:

- `fillReleaseNotesTemplate.js` - Line ~309
- `createAdoPrs.js` - Line ~450  
- `createReleaseBranch.js` - Line ~200

After testing locally, verify the pipeline still works by running a test build.

## Troubleshooting

### Common Error Messages

1. **"ENOENT: no such file or directory, scandir"**
   - Missing `_hashes` directory or hash files
   - **For testing**: Create mock hash files as shown above
   - **For production**: Hash files should be generated by the build process - this indicates a build failure

2. **"got.get is not a function"** 
   - Package API changed
   - Solution: Update code to use new API

3. **"Failed request: (401)"**
   - Authentication issue or API calls in dry-run
   - Solution: Check PAT and dry-run logic

4. **Node.js version warnings**
   - Using outdated Node.js version
   - Solution: Ensure Node.js 18+ for native fetch support

### Getting Help

- Check the Azure DevOps pipeline logs for real-world usage examples
- Review git history to see how similar issues were resolved
- Test with minimal reproduction cases before updating production dependencies
