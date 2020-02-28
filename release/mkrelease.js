const fs = require('fs');
const cp = require('child_process');
const naturalSort = require('natural-sort');
const path = require('path');

const { Octokit } = require("@octokit/rest");
const owner = 'microsoft';
const repo  = 'azure-pipelines-agent';
const octokit = new Octokit({}); // only read-only operations, no need to auth

const INTEGRATION_DIR = path.join(__dirname, '..', '_layout', 'integrations');
const GIT = 'git';
const VALID_RELEASE_RE = /^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$/;
const GIT_RELEASE_RE = /([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3})/;


process.env.EDITOR = process.env.EDITOR === undefined ? 'code --wait' : process.env.EDITOR;

var opt = require('node-getopt').create([
    ['',  'dryrun',               'Dry run only, do not actually commit new release'],
    ['',  'unattended',           'This is run in a pipeline, so do not prompt for confirmation of release notes'],
    ['',  'derivedFrom=version',  'Used to get PRs merged since this release was created', 'latest'],
    ['',  'branch=branch',        'Branch to select PRs merged into', 'master'],
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
    console.log(`Using node version ${version}`);
}

function verifyMinimumGitVersion()
{
    var gitVersionOutput = cp.execSync(GIT + ' --version', { encoding: 'utf-8'});
    if (gitVersionOutput == "")
    {
        console.log(`Unable to get Git Version. Got: ${gitVersionOutput}`);
        process.exit(-1);
    }
    var gitVersion = gitVersionOutput.match(GIT_RELEASE_RE)[0];

    var minimumGitVersion = "2.25.0"; // this is the version that supports sparse-checkout
    if (parseFloat(gitVersion) < parseFloat(minimumGitVersion))
    {
        console.log(`Version of Git does not meet minimum requirement of ${minimumGitVersion}`);
        process.exit(-1);
    }
    console.log(`Using git version ${gitVersion}`);

}

async function verifyNewReleaseTagOk(newRelease)
{
    if (!newRelease || !newRelease.match(VALID_RELEASE_RE) || newRelease.endsWith('.999.999'))
    {
        console.log(`Invalid version '${newRelease}'. Version must be in the form of <major>.<minor>.<patch> where each level is 0-999`);
        process.exit(-1);
    }
    try
    {
        var tag = 'v' + newRelease;
        await octokit.repos.getReleaseByTag({
            owner,
            repo,
            tag
        });

        console.log(`Version ${newRelease} is already in use`);
        process.exit(-1);
    }
    catch (e)
    {
        console.log(`Version ${newRelease} is available for use`);
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

    try
    {
        var tag = 'latest';
        if (derivedFrom !== 'latest')
        {
            tag = 'v' + derivedFrom;
        }
        var releaseInfo = await octokit.repos.getReleaseByTag({
            owner,
            repo,
            tag
        });

        var branch = opt.options.branch;
        var lastReleaseDate = releaseInfo.data.published_at;
        console.log(`Fetching PRs merged since ${lastReleaseDate} on ${branch}`);
        try
        {
            var results = await octokit.search.issuesAndPullRequests({
                q:`type:pr+is:merged+repo:${owner}/${repo}+base:${branch}+merged:>=${lastReleaseDate}`,
                order: 'asc',
                sort: 'created'
            })
            editReleaseNotesFile(results.data);
        }
        catch (e)
        {
            console.log(`Error: Problem fetching PRs: ${e}`);
            process.exit(-1);
        }
    }
    catch (e)
    {
        console.log(`Error: Cannot find release ${opt.options.derivedFrom}. Aborting.`);
        process.exit(-1);
    }
}

function editReleaseNotesFile(body)
{
    var releaseNotesFile = path.join(__dirname, '..', 'releaseNote.md');
    var existingReleaseNotes = fs.readFileSync(releaseNotesFile);
    var newPRs = { "Features": [], "Bugs": [], "Misc": [] };
    body.items.forEach(function (item) {
        var category = "Misc";
        item.labels.forEach(function (label) {
            if (category)
            {
                if (label.name === "bug")
                {
                    category = "Bugs";
                }
                if (label.name === "enhancement")
                {
                    category = "Features";
                }
                if (label.name === "internal")
                {
                    category = null;
                }
            }
        });
        if (category)
        {
            newPRs[category].push(` - ${item.title} (#${item.number})`);
        }
    });
    var newReleaseNotes = "";
    var categories = ["Features", "Bugs", "Misc"];
    categories.forEach(function (category) {
        newReleaseNotes += `## ${category}\n${newPRs[category].join("\n")}\n\n`;
    });

    newReleaseNotes += existingReleaseNotes;
    var editorCmd = process.env.EDITOR + ' ' + releaseNotesFile;
    console.log(editorCmd);
    if (opt.options.dryrun)
    {
        console.log("Found the following PRs = %o", newPRs);
        console.log("\n\n");
        console.log(newReleaseNotes);
        console.log("\n");
    }
    else
    {
        fs.writeFileSync(releaseNotesFile, newReleaseNotes);
        if (!opt.options.unattended)
        {
            try
            {
                cp.execSync(`${process.env.EDITOR} ${releaseNotesFile}`, {
                    stdio: [process.stdin, process.stdout, process.stderr]
                });
            }
            catch (err)
            {
                console.log(err.message);
                process.exit(-1);
            }
        }
        else
        {
            console.log('Skipping opening release notes in editor');
        }
    }
}

function versionifySync(template, destination, version)
{
    try
    {
        var data = fs.readFileSync(template, 'utf8');
        data = data.replace(/<AGENT_VERSION>/g, version);
        console.log(`Generating ${destination}`);
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
    execInForeground(`${GIT} checkout -b ${branch}`, directory);
    execInForeground(`${GIT} commit -m "Agent Release ${release}" `, directory);
    execInForeground(`${GIT} push --set-upstream origin ${branch}`, directory);
}

function commitAgentChanges(directory, release)
{
    var newBranch = "releases/" + release;
    execInForeground(`${GIT} add  ${path.join('src', 'agentversion')}`, directory);
    execInForeground(`${GIT} add releaseNote.md`, directory);
    commitAndPush(directory, release, newBranch);

    console.log("Create and publish release by kicking off this pipeline. (Use branch " + newBranch + ")");
    console.log("       https://dev.azure.com/mseng/AzureDevOps/_build?definitionId=5845 ");
    console.log("");
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
    execInForeground(`${GIT} sparse-checkout init --cone`, directory);
}

function commitADOL2Changes(directory, release)
{
    try
    {
        var gitUrl =  "https://mseng@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps"
        sparseClone(directory, gitUrl);
        var file = path.join(INTEGRATION_DIR, 'InstallAgentPackage.xml');
        var targetDirectory = path.join('DistributedTask', 'Service', 'Servicing', 'Host', 'Deployment', 'Groups');
        execInForeground(`${GIT} sparse-checkout set ${targetDirectory}`, directory);
        var target = path.join(directory, targetDirectory, 'InstallAgentPackage.xml');

        if (opt.options.dryrun)
        {
            console.log(`Copy file from ${file} to ${target}`);
        }
        else
        {
            fs.copyFileSync(file, target);
        }
        var newBranch = `users/${process.env.USER}/agent-${release}`;
        execInForeground(`${GIT} add ${targetDirectory}`, directory);
        commitAndPush(directory, release, newBranch);
        console.log("Create pull-request for this change ");
        console.log("       https://dev.azure.com/mseng/_git/AzureDevOps/pullrequests?_a=mine");
        console.log("");
    }
    catch (e)
    {
        console.log(`Error: Unable to create ADO L2 PR: ${e}`);
    }
}

function commitADOConfigChange(directory, release)
{
    try
    {
        var gitUrl =  "https://mseng@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps.ConfigChange"

        sparseClone(directory, gitUrl);
        execInForeground(`${GIT} sparse-checkout set tfs`, directory);
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
            console.log(`Copy file from ${path.join(INTEGRATION_DIR, targetDir)} to ${tfsDir}${milestoneDir}`);
        }
        else
        {
            fs.mkdirSync(path.join(tfsDir, milestoneDir, targetDir));
            fs.readdirSync(path.join(INTEGRATION_DIR, targetDir)).forEach( function (file) {
                fs.copyFileSync(path.join(INTEGRATION_DIR, targetDir, file), path.join(tfsDir, milestoneDir, file));
            });
        }

        var newBranch = "users/" + process.env.USER + "/agent-" + release;
        execInForeground(`${GIT} add ${path.join('tfs', milestoneDir)}`, directory);
        commitAndPush(directory, release, newBranch);

        console.log("Create pull-request for this change ");
        console.log("       https://dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps.ConfigChange/pullrequests?_a=mine");
        console.log("");
    }
    catch (e)
    {
        console.log(`Error: Unable to create ADO ConfigChange PR: ${e}`);
    }
}

function checkGitStatus()
{
    var git_status = cp.execSync(`${GIT} status --untracked-files=no --porcelain`, { encoding: 'utf-8'});
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
    verifyMinimumNodeVersion();
    verifyMinimumGitVersion();
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
