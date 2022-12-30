const azdev = require('azure-devops-node-api');
const fs = require('fs');
const path = require('path');
const tl = require('azure-pipelines-task-lib/task');
const util = require('./util');
const got = require('got');

const INTEGRATION_DIR = path.join(__dirname, '..', '_layout', 'integrations');
const GIT = 'git';

var opt = require('node-getopt').create([
    ['',  'dryrun',               'Dry run only, do not actually commit new release'],
    ['h', 'help',                 'Display this help'],
  ])
  .setHelp(
    'Usage: node createAdoPrs.js [OPTION] <version>\n' +
    '\n' +
    '[[OPTIONS]]\n'
  )
  .bindHelp()     // bind option 'help' to default action
  .parseSystem(); // parse command line

const authHandler = azdev.getPersonalAccessTokenHandler(process.env.PAT);
const connection = new azdev.WebApi('https://dev.azure.com/mseng', authHandler);

/**
 * Fills InstallAgentPackage.xml and Publish.ps1 templates.
 * Replaces <AGENT_VERSION> and <HASH_VALUE> with the right values in these files.
 * @param {string} newRelease Agent version, e.g. 2.193.0
 */
function createIntegrationFiles(newRelease)
{
    fs.mkdirSync(INTEGRATION_DIR, { recursive: true });
    util.fillAgentParameters(
        path.join(__dirname, '..', 'src', 'Misc', 'InstallAgentPackage.template.xml'),
        path.join(INTEGRATION_DIR, 'InstallAgentPackage.xml'),
        newRelease
    );
    util.fillAgentParameters(
        path.join(__dirname, '..', 'src', 'Misc', 'Publish.template.ps1'),
        path.join(INTEGRATION_DIR, 'Publish.ps1'),
        newRelease
    );
}

function commitAndPush(directory, release, branch)
{
    util.execInForeground(`${GIT} checkout -b ${branch}`, directory);
    util.execInForeground(`${GIT} commit -m "Agent Release ${release}" `, directory);
    util.execInForeground(`${GIT} push --set-upstream origin ${branch}`, directory);
}

function sparseClone(directory, url)
{
    if (fs.existsSync(directory))
    {
        console.log(`Removing previous clone of ${directory}`);
        if (!opt.options.dryrun)
        {
            fs.rmdirSync(directory, { recursive: true });
        }
    }

    util.execInForeground(`${GIT} clone --no-checkout --depth 1 ${url} ${directory}`, null, opt.dryrun);
    util.execInForeground(`${GIT} sparse-checkout init --cone`, directory, opt.dryrun);
}

async function createAdoPR(directory, release)
{
    var gitUrl = `https://${process.env.PAT}@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps`;

    var file = path.join(INTEGRATION_DIR, 'InstallAgentPackage.xml');
    var targetDirectory = path.join('DistributedTask', 'Service', 'Servicing', 'Host', 'Deployment', 'Groups');
    var target = path.join(directory, targetDirectory, 'InstallAgentPackage.xml');

    if (!fs.existsSync(directory))
    {
        // sparseClone(directory, gitUrl);
        // util.execInForeground(`${GIT} sparse-checkout set ${targetDirectory}`, directory, opt.dryrun);
        util.execInForeground(`${GIT} clone --depth 1 ${gitUrl} ${directory}`, null, opt.dryrun);
    }

    if (opt.options.dryrun)
    {
        console.log(`Copy file from ${file} to ${target}`);
    }
    else
    {
        fs.copyFileSync(file, target);
    }
    var newBranch = `users/${process.env.USERNAME}/agent-${release}`;
    util.execInForeground(`${GIT} add ${targetDirectory}`, directory, opt.dryrun);
    commitAndPush(directory, release, newBranch);

    console.log(`Creating pr from ${newBranch} into master in the AzureDevOps repo`);

    const gitApi = await connection.getGitApi();
    await gitApi.createPullRequest({
        sourceRefName: `refs/heads/${newBranch}`,
        targetRefName: 'refs/heads/master',
        title: 'Update agent',
        description: `Update agent to version ${release}`
    }, 'AzureDevOps', 'AzureDevOps');
}

async function createConfigChangePR(repoPath, agentVersion) {
    const gitUrl = `https://${process.env.PAT}@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps.ConfigChange`;

    if (!agentVersion.match(/^\d\.\d\d\d.\d+$/)) {
        throw new Error(`Agent version should fit the pattern: x.xxx.xxx; received: ${agentVersion}`);
    }

    if (!fs.existsSync(repoPath)) {
        util.execInForeground(`${GIT} clone --depth 1 ${gitUrl} ${repoPath}`, null, opt.dryrun);
    }

    const sprint = await getCurrentSprint();
    const publishScriptPathInRepo = path.join('tfs', `m${sprint}`, 'PipelinesAgentRelease', agentVersion, 'Publish.ps1');
    const publishScriptPathInSystem = path.join(repoPath, publishScriptPathInRepo);
    fs.mkdirSync(path.dirname(publishScriptPathInSystem), { recursive: true });

    const file = path.join(INTEGRATION_DIR, 'Publish.ps1');

    if (opt.options.dryrun) {
        console.log(`Fake copy file from ${file} to ${publishScriptPathInSystem}`);
    } else {
        console.log(`Copy file from ${file} to ${publishScriptPathInSystem}`);
        fs.copyFileSync(file, publishScriptPathInSystem);
    }

    const newBranch = `users/${process.env.USERNAME}/agent-${agentVersion}`;
    util.execInForeground(`${GIT} add ${publishScriptPathInRepo}`, repoPath, opt.dryrun);
    commitAndPush(repoPath, agentVersion, newBranch);

    console.log(`Creating pr from ${newBranch} into master in the AzureDevOps.ConfigChange repo`);

    const gitApi = await connection.getGitApi();
    const pullRequest = {
        sourceRefName: `refs/heads/${newBranch}`,
        targetRefName: 'refs/heads/master',
        title: 'Update agent',
        description: `Update agent publish script to version ${agentVersion}`
    };
    const repo = 'AzureDevOps.ConfigChange';
    const project = 'AzureDevOps';
    await gitApi.createPullRequest(pullRequest, repo, project);
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

async function main()
{
    try {
        var newRelease = opt.argv[0];
        if (newRelease === undefined)
        {
            console.log('Error: You must supply a version');
            process.exit(-1);
        }
        util.verifyMinimumNodeVersion();
        util.verifyMinimumGitVersion();
        createIntegrationFiles(newRelease);
        util.execInForeground(`${GIT} config --global user.email "${process.env.USEREMAIL}"`, null, opt.dryrun);
        util.execInForeground(`${GIT} config --global user.name "${process.env.USERNAME}"`, null, opt.dryrun);

        var pathToAdo = path.join(INTEGRATION_DIR, 'AzureDevOps');
        await createAdoPR(pathToAdo, newRelease);

        const pathToAdoConfigChange = path.join(INTEGRATION_DIR, 'AzureDevOps.ConfigChange');
        await createConfigChangePR(pathToAdoConfigChange, newRelease);

        console.log('done.');
    }
    catch (err) {
        tl.setResult(tl.TaskResult.Failed, err.message || 'run() failed', true);
        throw err;
    }
}

main();
