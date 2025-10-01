#!/usr/bin/env node

/**
 * Script to duplicate an existing GitHub Pull Request
 * Creates a new PR with the same title, body, and file changes
 * 
 * Usage: node duplicatePR.js <pr-number> [options]
 * 
 * Example: node duplicatePR.js 1234 --branch=duplicate-pr-1234
 */

const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const opt = require('node-getopt').create([
    ['', 'branch=ARG', 'Name for the new branch (default: duplicate-pr-{number})'],
    ['', 'dryrun', 'Dry run only, do not actually create PR'],
    ['h', 'help', 'Display this help'],
])
    .setHelp(
        'Usage: node duplicatePR.js <pr-number> [OPTION]\n' +
        '\n' +
        'Creates a new PR with the same changes, title, and description as an existing PR.\n' +
        '\n' +
        '[[OPTIONS]]\n'
    )
    .bindHelp()
    .parseSystem();

const GIT = 'git';

/**
 * Execute a command and return the output
 */
function execCommand(command, silent = false) {
    if (!silent) {
        console.log(`Executing: ${command}`);
    }
    try {
        return execSync(command, { encoding: 'utf-8', stdio: silent ? 'pipe' : 'inherit' });
    } catch (error) {
        console.error(`Command failed: ${command}`);
        throw error;
    }
}

/**
 * Execute a git command
 */
function execGit(args, silent = false) {
    return execCommand(`${GIT} ${args}`, silent);
}

/**
 * Get PR details using gh CLI
 */
function getPRDetails(prNumber) {
    console.log(`Fetching details for PR #${prNumber}...`);
    const output = execCommand(
        `gh pr view ${prNumber} --json number,title,body,headRefName,baseRefName,state,files`,
        true
    );
    return JSON.parse(output);
}

/**
 * Get the diff for a specific PR
 */
function getPRDiff(prNumber) {
    console.log(`Fetching diff for PR #${prNumber}...`);
    return execCommand(`gh pr diff ${prNumber}`, true);
}

/**
 * Create a new branch
 */
function createBranch(branchName) {
    console.log(`Creating new branch: ${branchName}`);
    execGit(`checkout -b ${branchName}`);
}

/**
 * Apply patch to current branch
 */
function applyPatch(patchContent) {
    console.log('Applying patch to new branch...');
    const tempPatchFile = path.join('/tmp', `pr-patch-${Date.now()}.patch`);
    fs.writeFileSync(tempPatchFile, patchContent);
    
    try {
        execGit(`apply ${tempPatchFile}`);
        console.log('Patch applied successfully');
    } catch (error) {
        console.error('Failed to apply patch');
        throw error;
    } finally {
        if (fs.existsSync(tempPatchFile)) {
            fs.unlinkSync(tempPatchFile);
        }
    }
}

/**
 * Stage and commit changes
 */
function commitChanges(message) {
    console.log('Staging changes...');
    execGit('add .');
    
    console.log('Committing changes...');
    execGit(`commit -m "${message}"`);
}

/**
 * Push branch to remote
 */
function pushBranch(branchName) {
    console.log(`Pushing branch ${branchName} to remote...`);
    execGit(`push -u origin ${branchName}`);
}

/**
 * Create a new PR
 */
function createPR(title, body, baseBranch) {
    console.log('Creating new pull request...');
    const escapedBody = body.replace(/"/g, '\\"').replace(/\n/g, '\\n');
    execCommand(`gh pr create --title "${title}" --body "${escapedBody}" --base ${baseBranch}`);
}

/**
 * Main function
 */
async function main() {
    try {
        const prNumber = opt.argv[0];
        if (!prNumber) {
            console.error('Error: You must supply a PR number');
            console.log('Usage: node duplicatePR.js <pr-number> [options]');
            process.exit(1);
        }

        const dryrun = opt.options.dryrun || false;
        const branchName = opt.options.branch || `duplicate-pr-${prNumber}`;

        console.log(`=== Duplicating PR #${prNumber} ===`);
        console.log(`Dry run: ${dryrun}`);
        console.log(`New branch name: ${branchName}`);
        console.log();

        // Get PR details
        const prDetails = getPRDetails(prNumber);
        
        console.log('PR Details:');
        console.log(`  Title: ${prDetails.title}`);
        console.log(`  Base: ${prDetails.baseRefName}`);
        console.log(`  Head: ${prDetails.headRefName}`);
        console.log(`  State: ${prDetails.state}`);
        console.log(`  Files changed: ${prDetails.files.length}`);
        console.log();

        if (prDetails.state !== 'OPEN' && prDetails.state !== 'CLOSED') {
            console.warn(`Warning: PR is in state ${prDetails.state}`);
        }

        // Get the diff
        const diff = getPRDiff(prNumber);
        
        if (dryrun) {
            console.log('=== DRY RUN MODE ===');
            console.log('Would create branch:', branchName);
            console.log('Would apply patch with', diff.split('\n').length, 'lines');
            console.log('Would commit with message:', prDetails.title);
            console.log('Would push branch:', branchName);
            console.log('Would create PR with:');
            console.log('  Title:', prDetails.title);
            console.log('  Base:', prDetails.baseRefName);
            console.log('  Body:', prDetails.body || '(empty)');
            return;
        }

        // Check for uncommitted changes
        const status = execGit('status --porcelain', true);
        if (status.trim()) {
            console.error('Error: You have uncommitted changes. Please commit or stash them first.');
            process.exit(1);
        }

        // Checkout base branch and pull latest
        console.log(`Checking out base branch: ${prDetails.baseRefName}`);
        execGit(`checkout ${prDetails.baseRefName}`);
        execGit('pull');

        // Create new branch
        createBranch(branchName);

        // Apply the patch
        applyPatch(diff);

        // Commit changes
        commitChanges(prDetails.title);

        // Push branch
        pushBranch(branchName);

        // Create PR
        const newBody = `This PR is a duplicate of #${prNumber}\n\n---\n\n${prDetails.body || ''}`;
        createPR(prDetails.title, newBody, prDetails.baseRefName);

        console.log();
        console.log('=== SUCCESS ===');
        console.log(`Successfully created a duplicate of PR #${prNumber}`);
        console.log(`New branch: ${branchName}`);

    } catch (error) {
        console.error('Error:', error.message);
        process.exit(1);
    }
}

main();
