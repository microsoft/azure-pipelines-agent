const fs = require('fs');
const cp = require('child_process');
const naturalSort = require('natural-sort');
const path = require('path');
const httpm = require('typed-rest-client/HttpClient');

const INTEGRATION_DIR = path.join(__dirname, '..', '_layout', 'integrations');
const GIT = 'git';
const VALID_RELEASE_RE = /^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$/;
const GIT_HUB_API_URL_ROOT="https://api.github.com/repos/microsoft/azure-pipelines-agent";

var httpc = new httpm.HttpClient('vsts-node-api');

process.env.EDITOR = process.env.EDITOR === undefined ? 'code --wait' : process.env.EDITOR;

var opt = require('node-getopt').create([
    ['',  'dryrun',               'Dry run only, do not actually commit new release'],
    ['',  'derivedFrom=version',  'Used to get PRs merged since this release was created', 'latest'],
    ['h', 'help',                 'Display this help'],
  ])
  .setHelp(
    "Usage: node mkrelease.js [OPTION] <version>\n" +
    "\n" +
    "[[OPTIONS]]\n"
  )
  .bindHelp()     // bind option 'help' to default action
  .parseSystem(); // parse command line


async function verifyNewReleaseTagOk(newRelease)
{
    if (!newRelease || !newRelease.match(VALID_RELEASE_RE) || newRelease.endsWith('.999.999'))
    {
        console.log("Invalid version '" + newRelease + "'. Version must be in the form of <major>.<minor>.<patch> where each level is 0-999");
        process.exit(-1);
    }
    var body = await (await httpc.get(GIT_HUB_API_URL_ROOT + "/releases/tags/v" + newRelease)).readBody();
    body = JSON.parse(body);
    if (body.message !== "Not Found")
    {
        console.log("Version " + newRelease + " is already in use");
        process.exit(-1);
    }
    else
    {
        console.log("Version " + newRelease + " is available for use");
    }
}

function writeAgentVersionFile(newRelease)
{
    console.log("Writing agent version file")
    if (!opt.options.dryrun)
    {
        fs.writeFileSync(path.join(__dirname, '..', 'src', 'agentversion'), newRelease  + "\n");
    }
    return newRelease;
}

async function fetchPRsSinceLastReleaseAndEditReleaseNotes(newRelease, callback)
{
    var derivedFrom = opt.options.derivedFrom;
    console.log("Derived from %o", derivedFrom);
    if (derivedFrom !== 'latest')
    {
        if (!derivedFrom.startsWith('v'))
        {
            derivedFrom = 'v' + derivedFrom;
        }
        derivedFrom = 'tags/' + derivedFrom;
    }

    var body = await (await httpc.get(GIT_HUB_API_URL_ROOT + "/releases/" + derivedFrom)).readBody();
    body = JSON.parse(body);
    if (body.published_at === undefined)
    {
        console.log('Error: Cannot find release ' + opt.options.derivedFrom + '. Aborting.');
        process.exit(-1);
    }
    var lastReleaseDate = body.published_at;
    console.log("Fetching PRs merged since " + lastReleaseDate);
    body = await (await httpc.get("https://api.github.com/search/issues?q=type:pr+is:merged+repo:microsoft/azure-pipelines-agent+merged:>=" + lastReleaseDate + "&sort=closed_at&order=asc")).readBody();
    body = JSON.parse(body);
    editReleaseNotesFile(body);
}

function editReleaseNotesFile(body)
{
    var releaseNotesFile = path.join(__dirname, '..', 'releaseNote.md');
    var existingReleaseNotes = fs.readFileSync(releaseNotesFile);
    var newPRs = [];
    body.items.forEach(function (item) {
        newPRs.push(' - ' + item.title + ' (#' + item.number + ')');
    });
    var newReleaseNotes = newPRs.join("\n") + "\n\n" + existingReleaseNotes;
    var editorCmd = process.env.EDITOR + ' ' + releaseNotesFile;
    console.log(editorCmd);
    if (opt.options.dryrun)
    {
        console.log("Found the following PRs = %o", newPRs);
    }
    else
    {
        fs.writeFileSync(releaseNotesFile, newReleaseNotes);
        try
        {
            cp.execSync(process.env.EDITOR + ' ' + releaseNotesFile, {
                stdio: [process.stdin, process.stdout, process.stderr]
            });
        }
        catch (err)
        {
            console.log(err.message);
            process.exit(-1);
        }
    }
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

function commitAgentChanges(directory, release)
{
    var newBranch = "releases/" + release;
    execInForeground(GIT + " add " + path.join('src', 'agentversion'), directory);
    execInForeground(GIT + " add releaseNote.md", directory);
    commitAndPush(directory, release, newBranch);

    console.log("Create and publish release by kicking off this pipeline. (Use branch " + newBranch + ")");
    console.log("       https://dev.azure.com/mseng/AzureDevOps/_build?definitionId=5845 ");
    console.log("");
}

function cloneOrPull(directory, url)
{
    if (fs.existsSync(directory))
    {
        execInForeground(GIT + " checkout master", directory);
        execInForeground(GIT + " pull --depth 1", directory);
    }
    else
    {
        execInForeground(GIT + " clone --depth 1 " + url + " " + directory);
    }
}

function commitADOL2Changes(directory, release)
{
    var gitUrl =  "https://mseng@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps"

    cloneOrPull(directory, gitUrl);
    var file = path.join(INTEGRATION_DIR, 'InstallAgentPackage.xml');
    var target = path.join(directory, 'DistributedTask', 'Service', 'Servicing', 'Host', 'Deployment', 'Groups', 'InstallAgentPackage.xml');
    if (opt.options.dryrun)
    {
        console.log("Copy file from " + file + " to " + target );
    }
    else
    {
        fs.copyFileSync(file, target);
    }
    var newBranch = "users/" + process.env.USER + "/agent-" + release;
    execInForeground(GIT + " add DistributedTask", directory);
    commitAndPush(directory, release, newBranch);

    console.log("Create pull-request for this change ");
    console.log("       https://dev.azure.com/mseng/_git/AzureDevOps/pullrequests?_a=mine");
    console.log("");
}

function commitADOConfigChange(directory, release)
{
    var gitUrl =  "https://mseng@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps.ConfigChange"

    cloneOrPull(directory, gitUrl);
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

function checkGitStatus()
{
    var git_status = cp.execSync(GIT + ' status --untracked-files=no --porcelain', { encoding: 'utf-8'});
    if (git_status !== "")
    {
        console.log("You have uncommited changes in this clone. Aborting.");
        console.log(git_status);
        if (!opt.options.dryrun)
        {
            process.exit(-1);
        }
    }
    else
    {
        console.log("Git repo is clean.");
    }
    return git_status;
}

async function main()
{
    var newRelease = opt.argv[0];
    if (newRelease === undefined)
    {
        console.log('Error: You must supply a version');
        process.exit(-1);
    }
    await verifyNewReleaseTagOk(newRelease);
    checkGitStatus();
    writeAgentVersionFile(newRelease);
    await fetchPRsSinceLastReleaseAndEditReleaseNotes(newRelease);
    createIntegrationFiles(newRelease);
    commitAgentChanges(path.join(__dirname, '..'), newRelease);
    commitADOL2Changes(path.join(INTEGRATION_DIR, "AzureDevOps"), newRelease);
    commitADOConfigChange(path.join(INTEGRATION_DIR, "AzureDevOps.ConfigChange"), newRelease);
    console.log('done.');
}

main();
