const azdev = require('azure-devops-node-api');
const fs = require('fs');
const naturalSort = require('natural-sort');
const path = require('path');
const tl = require('azure-pipelines-task-lib/task');
const util = require('./util');

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

function createIntegrationFiles(newRelease, callback)
{
    fs.mkdirSync(INTEGRATION_DIR, { recursive: true });
    fs.readdirSync(INTEGRATION_DIR).forEach( function(entry) {
        if (entry.startsWith('PublishVSTSAgent-'))
        {
            // node 12 has recursive support in rmdirSync
            // but since most of us are still on node 10
            // remove the files manually first
            var dirToDelete = path.join(INTEGRATION_DIR, entry);
            fs.readdirSync(dirToDelete).forEach( function(file) {
                fs.unlinkSync(path.join(dirToDelete, file));
            });
            fs.rmdirSync(dirToDelete, { recursive: true });
        }
    });

    util.versionifySync(path.join(__dirname, '..', 'src', 'Misc', 'InstallAgentPackage.template.xml'),
        path.join(INTEGRATION_DIR, 'InstallAgentPackage.xml'),
        newRelease
    );
    var agentVersionPath=newRelease.replace(/\./g, '-');
    var publishDir = path.join(INTEGRATION_DIR, `PublishVSTSAgent-${agentVersionPath}`);
    fs.mkdirSync(publishDir, { recursive: true });

    util.versionifySync(path.join(__dirname, '..', 'src', 'Misc', 'PublishVSTSAgent.template.ps1'),
        path.join(publishDir, `PublishVSTSAgent-${agentVersionPath}.ps1`),
        newRelease
    );
    util.versionifySync(path.join(__dirname, '..', 'src', 'Misc', 'UnpublishVSTSAgent.template.ps1'),
        path.join(publishDir, `UnpublishVSTSAgent-${agentVersionPath}.ps1`),
        newRelease
    );
}

commitAndPush = function(directory, release, branch)
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

async function commitADOL2Changes(directory, release)
{
    var gitUrl =  `https://${process.env.PAT}@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps`

    var file = path.join(INTEGRATION_DIR, 'InstallAgentPackage.xml');
    var targetDirectory = path.join('DistributedTask', 'Service', 'Servicing', 'Host', 'Deployment', 'Groups');
    var target = path.join(directory, targetDirectory, 'InstallAgentPackage.xml');
    
    if (!fs.existsSync(directory))
    {
        sparseClone(directory, gitUrl);    
        util.execInForeground(`${GIT} sparse-checkout set ${targetDirectory}`, directory, opt.dryrun);
    }

    if (opt.options.dryrun)
    {
        console.log(`Copy file from ${file} to ${target}`);
    }
    else
    {
        fs.copyFileSync(file, target);
    }
    var newBranch = `users/${process.env.USER}/agent-${release}`;
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

async function commitADOConfigChange(directory, release)
{
    var gitUrl =  `https://${process.env.PAT}@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps.ConfigChange`

    sparseClone(directory, gitUrl);
    util.execInForeground(`${GIT} sparse-checkout set tfs`, directory, opt.dryrun);
    var agentVersionPath=release.replace(/\./g, '-');
    var milestoneDir = 'mXXX';
    var tfsDir = path.join(directory, 'tfs');
    if (fs.existsSync(tfsDir))
    {
        var dirs = fs.readdirSync(tfsDir, { withFileTypes: true })
        .filter(dirent => dirent.isDirectory() && dirent.name.startsWith('m'))
        .map(dirent => dirent.name)
        .sort(naturalSort({direction: 'desc'}))
        milestoneDir = dirs[0];
    }
    var targetDir = `PublishVSTSAgent-${agentVersionPath}`;
    if (opt.options.dryrun)
    {
        console.log(`Copy file from ${path.join(INTEGRATION_DIR, targetDir)} to ${tfsDir}${milestoneDir}`);
    }
    else
    {
        fs.mkdirSync(path.join(tfsDir, milestoneDir, targetDir));
        fs.readdirSync(path.join(INTEGRATION_DIR, targetDir)).forEach( function (file) {
            fs.copyFileSync(path.join(INTEGRATION_DIR, targetDir, file), path.join(tfsDir, milestoneDir, file));
        });
    }

    var newBranch = `users/${process.env.USER}/agent-${release}`;
    util.execInForeground(`${GIT} add ${path.join('tfs', milestoneDir)}`, directory, opt.dryrun);
    commitAndPush(directory, release, newBranch);

    console.log(`Creating pr from refs/heads/${newBranch} into refs/heads/master in the AzureDevOps.ConfigChange repo`);

    const gitApi = await connection.getGitApi();
    await gitApi.createPullRequest({
        sourceRefName: `refs/heads/${newBranch}`,
        targetRefName: 'refs/heads/master',
        title: 'Update agent',
        description: `Update agent to version ${release}`
    }, 'AzureDevOps.ConfigChange', 'AzureDevOps');
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
        var pathToAdo = path.join(INTEGRATION_DIR, 'AzureDevOps');
        var pathToConfigChange = path.join(INTEGRATION_DIR, 'AzureDevOps.ConfigChange');
        util.verifyMinimumNodeVersion();
        util.verifyMinimumGitVersion();
        createIntegrationFiles(newRelease);
        util.execInForeground(`${GIT} config --global user.email "${process.env.USER}@microsoft.com"`, null, opt.dryrun);
        util.execInForeground(`${GIT} config --global user.name "${process.env.USER}"`, null, opt.dryrun);
        await commitADOL2Changes(pathToAdo, newRelease);
        await commitADOConfigChange(pathToConfigChange, newRelease);
        console.log('done.');
    }
    catch (err) {
        tl.setResult(tl.TaskResult.Failed, err.message || 'run() failed', true);
        throw err;
    }
}

main();