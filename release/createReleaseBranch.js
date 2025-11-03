const cp = require('child_process');
const fs = require('fs');
const path = require('path');
const tl = require('azure-pipelines-task-lib/task');
const util = require('./util');

const { Octokit } = require("@octokit/rest");
const { graphql } = require("@octokit/graphql");
const fetch = require('node-fetch');

const OWNER = 'microsoft';
const REPO = 'azure-pipelines-agent';
const GIT = 'git';
const VALID_RELEASE_RE = /^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$/;
const octokit = new Octokit({}); // only read-only operations, no need to auth

/**
 * Parses version tag into components
 * @param {string} tag - Version tag (e.g., "v4.265.2")
 * @returns {object} Parsed version components
 */
function parseVersion(tag) {
    const match = tag.match(/v(\d+)\.(\d+)\.(\d+)/);
    if (!match) {
        throw new Error(`Invalid version tag format: ${tag}`);
    }
    return {
        major: parseInt(match[1]),
        sprint: parseInt(match[2]),
        patch: parseInt(match[3])
    };
}

/**
 * Determines release type based on version pattern
 * @param {string} version - Version string (e.g., "4.265.0" or "4.265.2")
 * @returns {object} Release metadata
 */
function getReleaseMetadata(version) {
    const parts = version.split('.');
    const major = parts[0];
    const sprint = parts[1];
    const patch = parseInt(parts[2]);
    
    const isSprintRelease = patch === 0;
    
    return {
        type: isSprintRelease ? 'SPRINT_RELEASE' : 'MID_SPRINT_RELEASE',
        major: major,
        sprint: sprint,
        patch: patch,
        version: version,
        
        // For sprint release: releases/4.265.0
        // For mid-sprint: releases/4.265.2
        targetBranch: `releases/${version}`,
        
        // For sprint release: v4. (find latest in major version)
        // For mid-sprint: v4.265.0 (the sprint base)
        baseTagPattern: isSprintRelease 
            ? `v${major}.` 
            : `v${major}.${sprint}.0`,
        
        isSprintRelease: isSprintRelease
    };
}

/**
 * Finds the most recent release before the current version
 * Algorithm: Look in current sprint first, then fall back to previous sprints
 */
function findBaseRelease(metadata, allReleases) {
    const publishedReleases = allReleases.filter(r => !r.draft);
    const majorPrefix = `v${metadata.major}.`;
    
    // Get all releases in same major version, sorted by sprint DESC then patch DESC
    const candidates = publishedReleases
        .filter(r => r.tag_name.startsWith(majorPrefix))
        .map(r => ({ release: r, version: parseVersion(r.tag_name) }))
        .filter(r => 
            r.version.sprint < parseInt(metadata.sprint) ||
            (r.version.sprint === parseInt(metadata.sprint) && r.version.patch < metadata.patch)
        )
        .sort((a, b) => {
            if (a.version.sprint !== b.version.sprint) {
                return b.version.sprint - a.version.sprint;
            }
            return b.version.patch - a.version.patch;
        });
    
    if (candidates.length === 0) {
        throw new Error(`No previous release found for ${metadata.version} in major version ${metadata.major}.`);
    }
    
    const base = candidates[0];
    console.log(`Base release: ${base.release.tag_name} (published ${base.release.published_at})`);
    return base.release;
}

/**
 * Determines which branch to compare against
 */
function getTargetBranch(metadata, optionBranch) {
    if (optionBranch && optionBranch !== 'master') {
        return optionBranch;
    }
    return metadata.isSprintRelease ? 'master' : `releases/${metadata.version}`;
}

/**
 * Validates prerequisites for the release
 */
async function validateReleasePrerequisites(metadata, targetBranch) {
    const errors = [];
    
    // Check 1: Target branch exists (except master)
    if (targetBranch !== 'master') {
        try {
            await octokit.repos.getBranch({ owner: OWNER, repo: REPO, branch: targetBranch });
        } catch (e) {
            errors.push(`Target branch '${targetBranch}' does not exist.`);
        }
    }
    
    // Check 2: Version doesn't already exist
    try {
        await octokit.repos.getReleaseByTag({ owner: OWNER, repo: REPO, tag: `v${metadata.version}` });
        errors.push(`Release v${metadata.version} already exists!`);
    } catch (e) {
        // Good - release doesn't exist
    }
    
    if (errors.length > 0) {
        console.error('\n=== Validation Errors ===');
        errors.forEach(err => console.error(`❌ ${err}`));
        console.error('');
        process.exit(-1);
    }
    
    console.log('✅ All prerequisites validated\n');
}

const graphqlWithFetch = graphql.defaults({ // Create a reusable GraphQL instance with fetch
    request: {
        fetch,
    },
    headers: {
        authorization: process.env.PAT ? `token ${process.env.PAT}` : undefined,
    }
});

process.env.EDITOR = process.env.EDITOR === undefined ? 'code --wait' : process.env.EDITOR;

var opt = require('node-getopt').create([
    ['', 'dryrun', 'Dry run only, do not actually commit new release'],
    ['', 'derivedFrom=version', 'DEPRECATED: Release type is auto-detected. Use "lastMinorRelease" for default behavior', 'lastMinorRelease'],
    ['', 'branch=branch', 'Branch to select PRs merged into (auto-detected: master for sprint, releases/x.y.z for mid-sprint)', 'master'],
    ['', 'targetCommitId=commit', 'Fetch PRs merged since this commit', ''],
    ['h', 'help', 'Display this help'],
])
    .setHelp(
        'Usage: node createReleaseBranch.js [OPTION] <version>\n' +
        '\n' +
        'Creates a release branch and generates release notes.\n' +
        '\n' +
        'Release Types (auto-detected from version):\n' +
        '  Sprint Release (x.y.0):     Monthly release from master\n' +
        '  Mid-Sprint Release (x.y.z): Urgent release from release branch (z > 0)\n' +
        '\n' +
        'Examples:\n' +
        '  node createReleaseBranch.js 4.265.0              # Sprint release from master\n' +
        '  node createReleaseBranch.js 4.265.2              # Mid-sprint release from releases/4.265.2\n' +
        '\n' +
        '[[OPTIONS]]\n'
    )
    .bindHelp()     // bind option 'help' to default action
    .parseSystem(); // parse command line

async function verifyNewReleaseTagOk(newRelease) {
    if (!newRelease || !newRelease.match(VALID_RELEASE_RE) || newRelease.endsWith('.999.999')) {
        console.log(`Invalid version '${newRelease}'. Version must be in the form of <major>.<minor>.<patch> where each level is 0-999`);
        process.exit(-1);
    }
    try {
        var tag = 'v' + newRelease;
        await octokit.repos.getReleaseByTag({
            owner: OWNER,
            repo: REPO,
            tag: tag
        });

        console.log(`Version ${newRelease} is already in use`);
        process.exit(-1);
    }
    catch {
        console.log(`Version ${newRelease} is available for use`);
    }
}

function writeAgentVersionFile(newRelease) {
    console.log('Writing agent version file')
    if (!opt.options.dryrun) {
        fs.writeFileSync(path.join(__dirname, '..', 'src', 'agentversion'), `${newRelease}\n`);
    }
    return newRelease;
}

function filterCommitsUpToTarget(commitList) {
    try{
        var targetCommitId = opt.options.targetCommitId;
        var targetIndex = commitList.indexOf(targetCommitId);

        if (targetIndex === -1) {
            console.log(`Debug: Commit ID ${targetCommitId} not found in the list.`);
            return commitList;
        }
        // Return commits up to and including the target commit
        return commitList.slice(0, targetIndex + 1);
    }catch (e){
        console.log(e);
        console.error(`Unexpected error while filtering commits`);
        process.exit(-1);
    }
}

async function fetchPRsForSHAsGraphQL(commitSHAs) {
    
    // Handle empty commits array
    if (!commitSHAs || commitSHAs.length === 0) {
        console.log('No commits to process');
        return [];
    }

    var queryParts = commitSHAs.map((sha, index) => `
    commit${index + 1}: object(expression: "${sha}") { ... on Commit { associatedPullRequests(first: 1) { 
    edges { node { title number createdAt closedAt labels(first: 10) { edges { node { name } } } } } } } }`);

    var fullQuery = `
        query ($repo: String!, $owner: String!) {
          repository(name: $repo, owner: $owner) {
            ${queryParts.join('\n')}
          }
        }
    `;

    try {
        var response = await graphqlWithFetch(fullQuery, {
            repo: REPO,
            owner: OWNER,
        });

        var prs = [];
        Object.keys(response.repository).forEach(commitKey => {
            var commit = response.repository[commitKey];
            if (commit && commit.associatedPullRequests) {
                commit.associatedPullRequests.edges.forEach(pr => {
                    prs.push({
                        title: pr.node.title,
                        number: pr.node.number,
                        createdAt: pr.node.createdAt,
                        closedAt: pr.node.closedAt,
                        labels: pr.node.labels.edges.map(label => ({ name: label.node.name })), // Extract label names
                    });
                });
            }
        });
        return prs;
    } catch (e) {
        console.log(e);
        console.error(`Error fetching PRs via GraphQL.`);
        process.exit(-1);
    }
}

async function fetchPRsSincePreviousReleaseAndEditReleaseNotes(newRelease, callback) {
    console.log(`\n=== Release Analysis for ${newRelease} ===`);
    
    // Step 1: Detect release type
    const metadata = getReleaseMetadata(newRelease);
    console.log(`Release Type: ${metadata.type}`);
    console.log(`Version: ${metadata.major}.${metadata.sprint}.${metadata.patch}`);
    
    try {
        // Step 2: Fetch all releases
        const allReleases = await octokit.repos.listReleases({
            owner: OWNER,
            repo: REPO
        });
        
        // Step 3: Find base release
        const baseRelease = findBaseRelease(metadata, allReleases.data);
        console.log(`Base Release: ${baseRelease.tag_name} (published ${baseRelease.published_at})`);
        
        // Step 4: Determine target branch
        const targetBranch = getTargetBranch(metadata, opt.options.branch);
        console.log(`Target Branch: ${targetBranch}`);
        console.log(`Comparison: ${baseRelease.tag_name}...${targetBranch}\n`);
        
        // Step 5: Validate prerequisites
        await validateReleasePrerequisites(metadata, targetBranch);
        
        // Step 6: Compare commits
        const comparison = await octokit.repos.compareCommits({
            owner: OWNER,
            repo: REPO,
            base: baseRelease.tag_name,
            head: targetBranch,
        });
        
        console.log(`Found ${comparison.data.commits.length} commits`);
        
        // Step 7: Filter commits (existing logic)
        const commitSHAs = comparison.data.commits.map(commit => commit.sha);
        const filteredCommits = filterCommitsUpToTarget(commitSHAs);
        
        if (filteredCommits.length === 0) {
            console.log(`Warning: No commits found between ${baseRelease.tag_name} and ${targetBranch}`);
            console.log(`This might indicate:`);
            if (metadata.isSprintRelease) {
                console.log(`  - No new PRs merged to master since last sprint`);
            } else {
                console.log(`  - No commits cherry-picked to ${targetBranch}`);
                console.log(`  - Branch ${targetBranch} might not exist or is identical to ${baseRelease.tag_name}`);
            }
        }
        
        // Step 8: Fetch PRs and generate release notes (existing logic)
        const allPRs = await fetchPRsForSHAsGraphQL(filteredCommits);
        editReleaseNotesFile({ items: allPRs });
        
    } catch (e) {
        console.log(e);
        console.log(`Error: Cannot process release. Aborting.`);
        process.exit(-1);
    }
}


async function fetchPRsSinceLastReleaseAndEditReleaseNotes(newRelease, callback) {
    var derivedFrom = opt.options.derivedFrom;
    
    // Add deprecation warning
    if (derivedFrom && derivedFrom !== 'lastMinorRelease') {
        console.log(`⚠️  WARNING: --derivedFrom=${derivedFrom} is deprecated and will be ignored.`);
        console.log(`   Release type is now auto-detected from version number.`);
        console.log(`   - Sprint releases (x.y.0) compare from master`);
        console.log(`   - Mid-sprint releases (x.y.z) compare from release branch\n`);
    }
    
    console.log("Derived from %o", derivedFrom);

    try {
        var releaseInfo;

        // If derivedFrom is 'lastMinorRelease', fetch PRs by comparing with the previous release.
        // For example:
        // - If newRelease = 4.255.0, it will compare changes with the latest RELEASE/PRE-RELEASE tag starting with 4.xxx.xxx.
        // - If newRelease = 3.255.1, it will compare changes with the latest RELEASE/PRE-RELEASE tag starting with 3.xxx.xxx.
        if (derivedFrom === 'lastMinorRelease') {
            console.log("Fetching PRs by comparing with the previous release.")
            await fetchPRsSincePreviousReleaseAndEditReleaseNotes(newRelease, callback);
            return;
        }
        else if (derivedFrom !== 'latest') {
            var tag = 'v' + derivedFrom;

            console.log(`Getting release by tag ${tag}`);

            releaseInfo = await octokit.repos.getReleaseByTag({
                owner: OWNER,
                repo: REPO,
                tag: tag
            });
        }
        else {
            console.log("Getting latest release");

            releaseInfo = await octokit.repos.getLatestRelease({
                owner: OWNER,
                repo: REPO
            });
        }

        var branch = opt.options.branch;
        var lastReleaseDate = releaseInfo.data.published_at;
        console.log(`Fetching PRs merged since ${lastReleaseDate} on ${branch}`);
        try {
            var results = await octokit.search.issuesAndPullRequests({
                q: `type:pr+is:merged+repo:${OWNER}/${REPO}+base:${branch}+merged:>=${lastReleaseDate}`,
                order: 'asc',
                sort: 'created'
            })
            editReleaseNotesFile(results.data);
        }
        catch (e) {
            console.log(`Error: Problem fetching PRs: ${e}`);
            process.exit(-1);
        }
    }
    catch (e) {
        console.log(e);
        console.log(`Error: Cannot find release ${opt.options.derivedFrom}. Aborting.`);
        process.exit(-1);
    }
}


function editReleaseNotesFile(body) {
    var releaseNotesFile = path.join(__dirname, '..', 'releaseNote.md');
    var existingReleaseNotes = fs.readFileSync(releaseNotesFile);
    var newPRs = { 'Features': [], 'Bugs': [], 'Misc': [] };
    body.items.forEach(function (item) {
        var category = 'Misc';
        item.labels.forEach(function (label) {
            if (category) {
                if (label.name === 'bug') {
                    category = 'Bugs';
                }
                if (label.name === 'enhancement') {
                    category = 'Features';
                }
                if (label.name === 'internal') {
                    category = null;
                }
            }
        });
        if (category) {
            newPRs[category].push(` - ${item.title} (#${item.number})`);
        }
    });
    var newReleaseNotes = '';
    var categories = ['Features', 'Bugs', 'Misc'];
    categories.forEach(function (category) {
        newReleaseNotes += `## ${category}\n${newPRs[category].join('\n')}\n\n`;
    });

    newReleaseNotes += existingReleaseNotes;
    var editorCmd = `${process.env.EDITOR} ${releaseNotesFile}`;
    console.log(editorCmd);
    if (opt.options.dryrun) {
        console.log('Found the following PRs = %o', newPRs);
        console.log('\n\n');
        console.log(newReleaseNotes);
        console.log('\n');
    }
    else {
        fs.writeFileSync(releaseNotesFile, newReleaseNotes);
        try {
            cp.execSync(`${process.env.EDITOR} ${releaseNotesFile}`, {
                stdio: [process.stdin, process.stdout, process.stderr]
            });
        }
        catch (err) {
            console.log(err.message);
            process.exit(-1);
        }
    }
}

function commitAndPush(directory, release, branch) {
    util.execInForeground(GIT + " checkout -b " + branch, directory, opt.options.dryrun);
    util.execInForeground(`${GIT} commit -m "Agent Release ${release}" `, directory, opt.options.dryrun);
    util.execInForeground(`${GIT} -c credential.helper='!f() { echo "username=pat"; echo "password=$PAT"; };f' push --set-upstream origin ${branch}`, directory, opt.options.dryrun);
}

function commitAgentChanges(directory, release) {
    var newBranch = `releases/${release}`;
    util.execInForeground(`${GIT} add ${path.join('src', 'agentversion')}`, directory, opt.options.dryrun);
    util.execInForeground(`${GIT} add releaseNote.md`, directory, opt.options.dryrun);
    util.execInForeground(`${GIT} config --global user.email "azure-pipelines-bot@microsoft.com"`, null, opt.options.dryrun);
    util.execInForeground(`${GIT} config --global user.name "azure-pipelines-bot"`, null, opt.options.dryrun);
    commitAndPush(directory, release, newBranch);
}

function checkGitStatus() {
    var git_status = cp.execSync(`${GIT} status --untracked-files=no --porcelain`, { encoding: 'utf-8' });
    if (git_status) {
        console.log('You have uncommited changes in this clone. Aborting.');
        console.log(git_status);
        if (!opt.options.dryrun) {
            process.exit(-1);
        }
    }
    else {
        console.log('Git repo is clean.');
    }
    return git_status;
}

async function main() {
    try {
        var newRelease = opt.argv[0];
        if (newRelease === undefined) {
            console.log('Error: You must supply a version');
            process.exit(-1);
        }
        util.verifyMinimumNodeVersion();
        util.verifyMinimumGitVersion();
        await verifyNewReleaseTagOk(newRelease);
        checkGitStatus();
        writeAgentVersionFile(newRelease);
        await fetchPRsSinceLastReleaseAndEditReleaseNotes(newRelease);
        commitAgentChanges(path.join(__dirname, '..'), newRelease);
        console.log('done.');
    }
    catch (err) {
        tl.setResult(tl.TaskResult.Failed, err.message || 'run() failed', true);
        throw err;
    }
}

main();