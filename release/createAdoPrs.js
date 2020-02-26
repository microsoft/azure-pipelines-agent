const fs = require('fs');
const cp = require('child_process');
const naturalSort = require('natural-sort');
const tl = require('azure-pipelines-task-lib/task');
const path = require('path');
const azdev = require('azure-devops-node-api');

const INTEGRATION_DIR = path.join(__dirname, '..', '_layout', 'integrations');
const GIT = 'git';
const GIT_RELEASE_RE = /([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3})/;

process.env.EDITOR = process.env.EDITOR === undefined ? 'code --wait' : process.env.EDITOR;

var opt = require('node-getopt').create([
    ['',  'dryrun',               'Dry run only, do not actually commit new release'],
    ['h', 'help',                 'Display this help'],
  ])
  .setHelp(
    "Usage: node mkrelease.js [OPTION] <version>\n" +
    "\n" +
    "[[OPTIONS]]\n"
  )
  .bindHelp()     // bind option 'help' to default action
  .parseSystem(); // parse command line

const authHandler = azdev.getPersonalAccessTokenHandler(process.env.TOKEN);
const connection = new azdev.WebApi('https://dev.azure.com/mseng', authHandler);
const gitApi = connection.getGitApi();

function verifyMinimumNodeVersion()
{
    var version = process.version;
    var minimumNodeVersion = "12.10.0"; // this is the version of node that supports the recursive option to rmdir
    if (parseFloat(version.substr(1,version.length)) < parseFloat(minimumNodeVersion))
    {
        console.log("Version of Node does not support recursive directory deletes. Be sure you are starting with a clean workspace!");

    }
    console.log("Using node version " + version);
}

function verifyMinimumGitVersion()
{
    var gitVersionOutput = cp.execSync(GIT + ' --version', { encoding: 'utf-8'});
    if (gitVersionOutput == "")
    {
        console.log("Unable to get Git Version. Got: " + gitVersionOutput);
        process.exit(-1);
    }
    var gitVersion = gitVersionOutput.match(GIT_RELEASE_RE)[0];

    var minimumGitVersion = "2.25.0"; // this is the version that supports sparse-checkout
    if (parseFloat(gitVersion) < parseFloat(minimumGitVersion))
    {
        console.log("Version of Git does not meet minimum requirement of " + minimumGitVersion);
        process.exit(-1);
    }
    console.log("Using git version " + gitVersion);

}

function execInForeground(command, directory)
{
    directory = directory === undefined ? "." : directory;
    console.log("% " + command);
    if (!opt.options.dryrun)
    {
        cp.execSync(command, { cwd: directory, stdio: [process.stdin, process.stdout, process.stderr] });
    }
}

function commitAndPush(directory, release, branch)
{
    execInForeground(GIT + " checkout -b " + branch, directory);
    execInForeground(`${GIT} commit -m "Agent Release ${release}" `, directory);
    execInForeground(`${GIT} push --set-upstream origin ${branch}`, directory);
}

function versionifySync(template, destination, version)
{
    try
    {
        var data = fs.readFileSync(template, 'utf8');
        data = data.replace(/<AGENT_VERSION>/g, version);
        console.log("Generating " + destination);
        fs.writeFileSync(destination, data);
    }
    catch(e)
    {
        console.log('Error:', e.stack);
    }
}

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

    versionifySync(path.join(__dirname, '..', 'src', 'Misc', 'InstallAgentPackage.template.xml'),
        path.join(INTEGRATION_DIR, "InstallAgentPackage.xml"),
        newRelease
    );
    var agentVersionPath=newRelease.replace(/\./g, '-');
    var publishDir = path.join(INTEGRATION_DIR, "PublishVSTSAgent-" + agentVersionPath);
    fs.mkdirSync(publishDir, { recursive: true });

    versionifySync(path.join(__dirname, '..', 'src', 'Misc', 'PublishVSTSAgent.template.ps1'),
        path.join(publishDir, "PublishVSTSAgent-" + agentVersionPath + ".ps1"),
        newRelease
    );
    versionifySync(path.join(__dirname, '..', 'src', 'Misc', 'UnpublishVSTSAgent.template.ps1'),
        path.join(publishDir, "UnpublishVSTSAgent-" + agentVersionPath + ".ps1"),
        newRelease
    );
}

function sparseClone(directory, url)
{
    if (fs.existsSync(directory))
    {
        console.log("Removing previous clone of " + directory);
        if (!opt.options.dryrun)
        {
            fs.rmdirSync(directory, { recursive: true });
        }
    }

    execInForeground(`${GIT} clone --no-checkout --depth 1 ${url} ${directory}`);
    execInForeground(GIT + " sparse-checkout init --cone", directory);
}

function commitADOL2Changes(directory, release)
{
    var gitUrl =  `https://${process.env.PAT}@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps`

    var file = path.join(INTEGRATION_DIR, 'InstallAgentPackage.xml');
    var targetDirectory = path.join('DistributedTask', 'Service', 'Servicing', 'Host', 'Deployment', 'Groups');
    var target = path.join(directory, targetDirectory, 'InstallAgentPackage.xml');
    
    if (!fs.existsSync(directory))
    {
        sparseClone(directory, gitUrl);    
        execInForeground(GIT + " sparse-checkout set " + targetDirectory, directory);
    }

    if (opt.options.dryrun)
    {
        console.log("Copy file from " + file + " to " + target );
    }
    else
    {
        fs.copyFileSync(file, target);
    }
    var newBranch = "users/" + process.env.USER + "/agent-" + release;
    execInForeground(GIT + " add " + targetDirectory, directory);
    commitAndPush(directory, release, newBranch);

    gitApi.createPullRequest({
        sourceRefName: newBranch,
        targetRefName: 'master',
        title: "Update agent",
        description: `Update agent to version ${release}`
    });
}

function commitADOConfigChange(directory, release)
{
    var gitUrl =  `https://${process.env.PAT}@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps.ConfigChange`

    sparseClone(directory, gitUrl);
    execInForeground(GIT + " sparse-checkout set tfs", directory);
    var agentVersionPath=release.replace(/\./g, '-');
    var milestoneDir = "mXXX";
    var tfsDir = path.join(directory, "tfs");
    if (fs.existsSync(tfsDir))
    {
        var dirs = fs.readdirSync(tfsDir, { withFileTypes: true })
        .filter(dirent => dirent.isDirectory() && dirent.name.startsWith("m"))
        .map(dirent => dirent.name)
        .sort(naturalSort({direction: 'desc'}))
        milestoneDir = dirs[0];
    }
    var targetDir = "PublishVSTSAgent-" + agentVersionPath;
    if (opt.options.dryrun)
    {
        console.log("Copy file from " + path.join(INTEGRATION_DIR, targetDir) + " to " + tfsDir + milestoneDir );
    }
    else
    {
        fs.mkdirSync(path.join(tfsDir, milestoneDir, targetDir));
        fs.readdirSync(path.join(INTEGRATION_DIR, targetDir)).forEach( function (file) {
            fs.copyFileSync(path.join(INTEGRATION_DIR, targetDir, file), path.join(tfsDir, milestoneDir, file));
        });
    }

    var newBranch = "users/" + process.env.USER + "/agent-" + release;
    execInForeground(GIT + " add " + path.join('tfs', milestoneDir), directory);
    commitAndPush(directory, release, newBranch);

    gitApi.createPullRequest({
        sourceRefName: newBranch,
        targetRefName: 'master',
        title: "Update agent",
        description: `Update agent to version ${release}`
    });
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
        var pathToAdo = path.join(INTEGRATION_DIR, "AzureDevOps");
        var pathToConfigChange = path.join(INTEGRATION_DIR, "AzureDevOps.ConfigChange");
        verifyMinimumNodeVersion();
        verifyMinimumGitVersion();
        createIntegrationFiles(newRelease);
        execInForeground(`${GIT} config --global user.email "${process.env.USER}@microsoft.com"`);
        execInForeground(`${GIT} config --global user.name "${process.env.USER}"`);
        commitADOL2Changes(pathToAdo, newRelease);
        commitADOConfigChange(pathToConfigChange, newRelease);
        console.log('done.');
    }
    catch (err) {
        tl.setResult(tl.TaskResult.Failed, err.message || 'run() failed', true);
        throw err;
    }
}

main();