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
    ['', 'derivedFrom=version', 'Used to get PRs merged since this release was created', 'lastMinorRelease'],
    ['', 'branch=branch', 'Branch to select PRs merged into', 'master'],
    ['h', 'help', 'Display this help'],
])
    .setHelp(
        'Usage: node createReleaseBranch.js [OPTION] <version>\n' +
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

async function fetchPRsForSHAsGraphQL(commitSHAs) {

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
    try {
        var latestReleases = await octokit.repos.listReleases({
            owner: OWNER,
            repo: REPO
        })

        var filteredReleases = latestReleases.data.filter(release => !release.draft); // consider only pre-releases and published releases

        var releaseTagPrefix = 'v' + newRelease.split('.')[0];
        console.log(`Getting latest release starting with ${releaseTagPrefix}`);

        var latestReleaseInfo = filteredReleases.find(release => release.tag_name.toLowerCase().startsWith(releaseTagPrefix.toLowerCase()));
        console.log(`Previous release tag with ${latestReleaseInfo.tag_name} and published date is: ${latestReleaseInfo.published_at}`)

        var headBranchTag = 'v' + newRelease
        try {
            var comparison = await octokit.repos.compareCommits({
                owner: OWNER,
                repo: REPO,
                base: latestReleaseInfo.tag_name,
                head: headBranchTag,
            });

            var commitSHAs = comparison.data.commits.map(commit => commit.sha);

            try {

                var allPRs = await fetchPRsForSHAsGraphQL(commitSHAs);
                editReleaseNotesFile({ items: allPRs });
            } catch (e) {
                console.log(e);
                console.log(`Error: Problem in fetching PRs using commit SHA. Aborting.`);
                process.exit(-1);
            }

        } catch (e) {
            console.log(e);
            console.log(`Error: Cannot find commits changes. Aborting.`);
            process.exit(-1);
        }
    }
    catch (e) {
        console.log(e);
        console.log(`Error: Cannot find releases. Aborting.`);
        process.exit(-1);
    }
}


async function fetchPRsSinceLastReleaseAndEditReleaseNotes(newRelease, callback) {
    var derivedFrom = opt.options.derivedFrom;
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