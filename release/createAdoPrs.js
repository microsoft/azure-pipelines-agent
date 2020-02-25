const fs = require('fs');
const cp = require('child_process');
const naturalSort = require('natural-sort');
const path = require('path');

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
    execInForeground(GIT + " push --set-upstream origin " + branch, directory);
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

    execInForeground(GIT + " clone --no-checkout --depth 1 " + url + " " + directory);
    execInForeground(GIT + " sparse-checkout init --cone", directory);
}

function commitADOL2Changes(directory, release)
{
    var gitUrl =  "https://mseng@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps"

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

    console.log("Create pull-request for this change ");
    console.log("       https://dev.azure.com/mseng/_git/AzureDevOps/pullrequests?_a=mine");
    console.log("");
}

function commitADOConfigChange(directory, release)
{
    var gitUrl =  "https://mseng@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps.ConfigChange"

    if (!fs.existsSync(directory))
    {
        sparseClone(directory, gitUrl);
        execInForeground(GIT + " sparse-checkout set tfs", directory);
    }
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

    console.log("Create pull-request for this change ");
    console.log("       https://dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps.ConfigChange/pullrequests?_a=mine");
    console.log("");
}

async function main()
{
    var newRelease = opt.argv[0];
    if (newRelease === undefined)
    {
        console.log('Error: You must supply a version');
        process.exit(-1);
    }
    var pathToAdo = opt.argv[1] || path.join(INTEGRATION_DIR, "AzureDevOps");
    var pathToConfigChange = opt.argv[2] || path.join(INTEGRATION_DIR, "AzureDevOps.ConfigChange");
    verifyMinimumNodeVersion();
    verifyMinimumGitVersion();
    commitADOL2Changes(pathToAdo, newRelease);
    commitADOConfigChange(pathToConfigChange, newRelease);
    console.log('done.');
}

main();
