const azdev = require('azure-devops-node-api');
const fs = require('fs');
const path = require('path');
const tl = require('azure-pipelines-task-lib/task');
const util = require('./util');
const got = require('got');

const INTEGRATION_DIR = path.join(__dirname, '..', '_layout', 'integrations');
const GIT = 'git';

const opt = require('node-getopt').create([
    ['', 'dryrun=ARG', 'Dry run only, do not actually commit new release'],
    ['h', 'help', 'Display this help'],
])
    .setHelp(
        'Usage: node createAdoPrs.js [OPTION] <version>\n' +
        '\n' +
        '[[OPTIONS]]\n'
    )
    .bindHelp()     // bind option 'help' to default action
    .parseSystem(); // parse command line

const orgUrl = 'dev.azure.com/mseng';
const httpsOrgUrl = `https://${orgUrl}`;
const authHandler = azdev.getPersonalAccessTokenHandler(process.env.PAT);

// Enable client-level retries so transient network errors (e.g. ECONNRESET) on
// read requests are retried automatically with backoff.
const connection = new azdev.WebApi(httpsOrgUrl, authHandler, { allowRetries: true, maxRetries: 5 });

// Network errors that are safe to retry. Mirrors typed-rest-client's NetworkRetryErrors.
const RETRYABLE_NETWORK_ERRORS = ['ECONNRESET', 'ENOTFOUND', 'ESOCKETTIMEDOUT', 'ETIMEDOUT', 'ECONNREFUSED', 'EPIPE'];

/**
 * Retries an async operation on transient network errors with exponential backoff.
 * Needed for non-idempotent POSTs that the HTTP client won't auto-retry.
 * @param {(attempt: number) => Promise<T>} operation Runs the operation; receives the 1-based attempt number
 * @param {string} description Label used for logging
 * @param {number} maxAttempts Max attempts including the first
 * @returns {Promise<T>}
 * @template T
 */
async function retryOnNetworkError(operation, description, maxAttempts = 5) {
    for (let attempt = 1; ; attempt++) {
        try {
            return await operation(attempt);
        } catch (err) {
            const code = err && err.code;
            const isTransient = code && RETRYABLE_NETWORK_ERRORS.indexOf(code) > -1;
            if (!isTransient || attempt >= maxAttempts) {
                throw err;
            }
            const delayMs = Math.min(1000 * Math.pow(2, attempt - 1), 30000);
            console.log(`${description} failed with transient network error "${code}" (attempt ${attempt}/${maxAttempts}). Retrying in ${delayMs}ms...`);
            await new Promise((resolve) => setTimeout(resolve, delayMs));
        }
    }
}

/**
 * Fills InstallAgentPackage.xml and Publish.ps1 templates.
 * Replaces <AGENT_VERSION> and <HASH_VALUE> with the right values in these files.
 * @param {string} agentVersion Agent version, e.g. 2.193.0
 */
function createIntegrationFiles(agentVersion) {
    fs.mkdirSync(INTEGRATION_DIR, { recursive: true });

    for (const agentPackageXml of ['InstallAgentPackage', 'UpdateAgentPackage']) {
        const xmlFilePath = path.join(INTEGRATION_DIR, `${agentPackageXml}.xml`);
        util.fillAgentParameters(
            path.join(__dirname, '..', 'src', 'Misc', `${agentPackageXml}.template.xml`),
            xmlFilePath,
            agentVersion
        );
        clearEmptyHashValueLine(xmlFilePath);
        clearEmptyXmlNodes(xmlFilePath);
    }

    const publishScriptFilePath = path.join(INTEGRATION_DIR, 'Publish.ps1');
    util.fillAgentParameters(
        path.join(__dirname, '..', 'src', 'Misc', 'Publish.template.ps1'),
        publishScriptFilePath,
        agentVersion
    );
    clearEmptyHashValueLine(publishScriptFilePath);
}

function clearEmptyXmlNodes(filePath) {
    let xmlFile = fs.readFileSync(filePath, 'utf-8');
    while (xmlFile.length != (xmlFile = xmlFile.replace(/\s*<[\w\s="]+>\n*\s*<\/[\w\s="]+>/g, "")).length) {
    }
    fs.writeFileSync(filePath, xmlFile);
}

function clearEmptyHashValueLine(filePath) {
    const text = fs.readFileSync(filePath, 'utf-8');
    const lines = text.split('\n');
    const modifiedLines = lines.filter((line) => !line.includes('<HASH_VALUE>'));
    fs.writeFileSync(filePath, modifiedLines.join('\n').replace(/\n\r(\n\r)+/g, '\n\r'));
}

/**
 * Create AzureDevOps pull request using files from `INTEGRATION_DIR`
 * @param {string} repo AzureDevOps repo name
 * @param {string} project AzureDevOps project name
 * @param {string} sourceBranch pull request source branch
 * @param {string} targetBranch pull request target branch
 * @param {string} commitMessage commit message
 * @param {string} title pull request title
 * @param {string} description pull reqest description
 * @param {string[]} targetsToCommit files to add in pull request
 */
async function openPR(repo, project, sourceBranch, targetBranch, commitMessage, title, description, targetsToCommit, dryrun = false) {
    console.log(`Creating PR from "${sourceBranch}" into "${targetBranch}" in the "${project}/${repo}" repo`);

    const repoPath = path.join(INTEGRATION_DIR, repo);

    if (!fs.existsSync(repoPath)) {
        const gitUrl = `https://${process.env.PAT}@${orgUrl}/${project}/_git/${repo}`;
        util.execInForeground(`${GIT} clone --depth 1 ${gitUrl} ${repoPath}`, null, dryrun);
    }

    for (const targetToCommit of targetsToCommit) {
        const relativePath = path.dirname(targetToCommit);
        const fullPath = path.join(repoPath, relativePath);
        const fileName = path.basename(targetToCommit);
        const sourceFile = path.join(INTEGRATION_DIR, fileName);
        const targetFile = path.join(fullPath, fileName);

        if (dryrun) {
            console.log(`Fake copy file from ${sourceFile} to ${targetFile}`);
        } else {
            console.log(`Copy file from ${sourceFile} to ${targetFile}`);
            fs.mkdirSync(fullPath, { recursive: true });
            fs.copyFileSync(sourceFile, targetFile);
        }
    }

    for (const targetToCommit of targetsToCommit) {
        util.execInForeground(`${GIT} add ${targetToCommit}`, repoPath, dryrun);
    }

    util.execInForeground(`${GIT} checkout -b ${sourceBranch}`, repoPath, dryrun);
    util.execInForeground(`${GIT} commit -m "${commitMessage}"`, repoPath, dryrun);
    util.execInForeground(`${GIT} push --force origin ${sourceBranch}`, repoPath, dryrun);

    const prefix = 'refs/heads/';

    const refs = {
        sourceRefName: `${prefix}${sourceBranch}`,
        targetRefName: `${prefix}${targetBranch}`
    };

    const pullRequest = { ...refs, title, description };

    console.log('Getting Git API');
    const gitApi = await retryOnNetworkError(() => connection.getGitApi(), 'Getting Git API');

    // Helper to look up an existing active PR for this source/target pair.
    const findExistingPR = async () => (await gitApi.getPullRequests(repo, refs, project))[0];

    console.log('Checking if an active pull request for the source and target branch already exists');
    let PR = await retryOnNetworkError(findExistingPR, 'Getting pull requests');

    if (PR) {
        console.log('PR already exists');
    } else if (dryrun) {
        return [-1, 'test']; // return without creating PR for test runs
    } else {
        console.log('PR does not exist; creating PR');
        // POST create is non-idempotent and not auto-retried. On retry, re-check
        // for an existing PR first to avoid ADO 409; skip this on attempt 1.
        PR = await retryOnNetworkError(async (attempt) => {
            if (attempt > 1) {
                const existing = await findExistingPR();
                if (existing) {
                    console.log('PR was already created by a previous attempt');
                    return existing;
                }
            }
            return gitApi.createPullRequest(pullRequest, repo, project);
        }, 'Creating pull request');
    }

    const prLink = `${httpsOrgUrl}/${project}/_git/${repo}/pullrequest/${PR.pullRequestId}`;
    console.log(`Link to the PR: ${prLink}`);

    return [PR.pullRequestId, prLink];
}

/**
 * Queries whatsprintis.it for current sprint version
 * 
 * @throws An error will be thrown if the response does not contain a sprint version as a three-digit numeric value
 * @returns current sprint version
 */
async function getCurrentSprint() {
    const response = await got.get('https://whatsprintis.it/?json', { responseType: 'json' });
    const sprint = response.body.sprint;
    if (!/^\d\d\d$/.test(sprint)) {
        throw new Error(`Sprint must be a three-digit number; received: ${sprint}`);
    }
    return sprint;
}

async function main() {
    try {
        const agentVersion = opt.argv[0];
        if (agentVersion === undefined) {
            console.log('Error: You must supply a version');
            process.exit(-1);
        } else if (!agentVersion.match(/^\d\.\d\d\d.\d+$/)) {
            throw new Error(`Agent version should fit the pattern: "x.xxx.xxx"; received: "${agentVersion}"`);
        }
        util.verifyMinimumNodeVersion();
        util.verifyMinimumGitVersion();
        createIntegrationFiles(agentVersion);

        let dryrun = false;
        if (opt.options.dryrun) {
            dryrun = opt.options.dryrun.toString().toLowerCase() === "true"
        }

        console.log(`Dry run: ${dryrun}`);

        util.execInForeground(`${GIT} config --global user.email "${process.env.USEREMAIL}"`, null, dryrun);
        util.execInForeground(`${GIT} config --global user.name "${process.env.USERNAME}"`, null, dryrun);

        const sprint = await getCurrentSprint();

        const project = 'AzureDevOps';
        const sourceBranch = `users/${process.env.USERNAME}/agent-${agentVersion}`;
        const targetBranch = 'master';
        const commitMessage = `Agent Release ${agentVersion}`;
        const title = 'Update Agent';

        const [adoPrId, adoPrLink] = await openPR(
            'AzureDevOps',
            project, sourceBranch, targetBranch, commitMessage, title,
            `Update agent to version ${agentVersion}`,
            [
                path.join(
                    'DistributedTask', 'Service', 'Servicing', 'Host', 'Deployment', 'Groups', 'InstallAgentPackage.xml'
                ),
                path.join(
                    'DistributedTask', 'Service', 'Servicing', 'Host', 'Deployment', 'Groups', 'UpdateAgentPackage.xml'
                ),
            ],
            dryrun
        );

        const [ccPrId, ccPrLink] = await openPR(
            'AzureDevOps.ConfigChange',
            project, sourceBranch, targetBranch, commitMessage, title,
            `Update agent publish script to version ${agentVersion}`,
            [
                path.join(
                    'tfs', `M${sprint}`, 'PipelinesAgentRelease', agentVersion, 'Publish.ps1'
                )
            ],
            dryrun
        );

        console.log(`##vso[task.setvariable variable=AdoPrId;isOutput=true]${adoPrId}`);
        console.log(`##vso[task.setvariable variable=AdoPrLink;isOutput=true]${adoPrLink}`);

        console.log(`##vso[task.setvariable variable=CcPrId;isOutput=true]${ccPrId}`);
        console.log(`##vso[task.setvariable variable=CcPrLink;isOutput=true]${ccPrLink}`);

        console.log('Done.');
    } catch (err) {
        tl.setResult(tl.TaskResult.Failed, err.message || 'run() failed', true);
        throw err;
    }
}

main();
