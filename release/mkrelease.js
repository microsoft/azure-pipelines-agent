const fs = require('fs');
const request = require('request');
const cp = require('child_process');
const mkpath = require('mkpath');
const naturalSort = require('natural-sort');

var gitHubRequest = request.defaults({
    headers: {'User-Agent': 'Request'}
})
const integrationDir = __dirname + '/../_layout/integrations';

const git = 'git';

const release_re = /^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$/;
const gitHubAPIURLRoot="https://api.github.com/repos/microsoft/azure-pipelines-agent";

process.env.EDITOR = process.env.EDITOR === undefined ? 'vi' : process.env.EDITOR;

opt = require('node-getopt').create([
    ['',  'dryrun',               'Dry run only, do not actually commit new release'],
    ['',  'derivedFrom=version',  'Used to get PRs merged since this release was created', 'latest'],
    ['h', 'help',                 'Display this help'],
  ])              // create Getopt instance
  .setHelp(
    "Usage: node mkrelease.js [OPTION] <version>\n" +
    "\n" +
    "[[OPTIONS]]\n"
  )
  .bindHelp()     // bind option 'help' to default action
  .parseSystem(); // parse command line


function verifyNewReleaseTagOk(newRelease, callback)
{
    if (newRelease === "" || !newRelease.match(release_re) || newRelease.endsWith('.999.999'))
    {
        console.log("Invalid version '" + newRelease + "'. Version must be in the form of <major>.<minor>.<patch> where each level is 0-999");
        process.exit(-1);
    }
    gitHubRequest(gitHubAPIURLRoot + "/releases/tags/v" + newRelease, { json: true }, function (err, resp, body) {
        if (err) throw err;
        if (body.message !== "Not Found")
        {
            console.log("Version " + newRelease + " is already in use");
            process.exit(-1)
        }
        else
        {
            console.log("Version " + newRelease + " is available for use");
        }
        callback();
    });
}

function writeAgentVersionFile(newRelease)
{
    console.log("Writing agent version file")
    if (!opt.options.dryrun)
    {
        fs.writeFileSync(__dirname + '/../src/agentversion', newRelease  + "\n");
    }
    return newRelease;
}

function fetchPRsSinceLastReleaseAndEditReleaseNotes(newRelease, callback)
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
    gitHubRequest(gitHubAPIURLRoot + "/releases/" + derivedFrom, { json: true }, function (err, resp, body) {
        if (err) throw err;
        if (body.published_at === undefined)
        {
            console.log('Error: Cannot find release ' + opt.options.derivedFrom + '. Aborting.');
            process.exit(-1);
        }
        var lastReleaseDate = body.published_at;
        console.log("Fetching PRs merged since " + lastReleaseDate);
        gitHubRequest("https://api.github.com/search/issues?q=type:pr+is:merged+repo:microsoft/azure-pipelines-agent+merged:>=" + lastReleaseDate + "&sort=closed_at&order=asc", { json: true }, function (err, resp, body) {
            editReleaseNotesFile(body, callback);
        });
    });
}

function editReleaseNotesFile(body, callback)
{
    var releaseNotesFile = __dirname + '/../releaseNote.md';
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
    callback();
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
    mkpath(integrationDir, function (err) {
        if (err) throw err;
        cp.execSync("rm  -rf " + integrationDir + "/PublishVSTSAgent-*");
        versionifySync(__dirname + "/../src/Misc/InstallAgentPackage.template.xml",
            integrationDir + "/InstallAgentPackage.xml",
            newRelease
        );
        var agentVersionPath=newRelease.replace('.', '-');
        var publishDir = integrationDir + "/PublishVSTSAgent-" + agentVersionPath
            mkpath(publishDir, function (err) {
                if (err) throw err;
                versionifySync(__dirname + "/../src/Misc/PublishVSTSAgent.template.ps1",
                publishDir + "/PublishVSTSAgent-" + agentVersionPath + ".ps1",
                newRelease
            );
            versionifySync(__dirname + "/../src/Misc/UnpublishVSTSAgent.template.ps1",
                publishDir + "/UnpublishVSTSAgent-" + agentVersionPath + ".ps1",
                newRelease
            );
            callback();
        });
    });
}

function execInForground(command, directory)
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
    execInForground(git + " checkout -b " + branch, directory);
    execInForground(git + " commit -m 'Agent Release " + release + "' ", directory);
    execInForground(git + " push --set-upstream origin " + branch, directory);
}

function commitAgentChanges(directory, release)
{
    var newBranch = "releases/" + release;
    execInForground(git + " add src/agentversion", directory);
    execInForground(git + " add releaseNote.md", directory);
    commitAndPush(directory, release, newBranch);

    console.log("Create and publish release by kicking off this pipeline. (Use branch " + newBranch + ")");
    console.log("       https://dev.azure.com/mseng/AzureDevOps/_build?definitionId=5845 ");
    console.log("");
}

function cloneOrPull(directory, url)
{
    if (fs.existsSync(directory))
    {
        execInForground(git + " checkout master", directory);
        execInForground(git + " pull", directory);
    }
    else
    {
        execInForground(git + " clone --depth 1 " + url + " " + directory);
    }
}

function commitADOL2Changes(directory, release)
{
    var gitUrl =  "https://mseng@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps"

    cloneOrPull(directory, gitUrl);
    if (opt.options.dryrun)
    {
        console.log("Copy file from " + integrationDir + "/InstallAgentPackage.xml" + " to " + directory + "/DistributedTask/Service/Servicing/Host/Deployment/Groups/InstallAgentPackage.xml" );
    }
    else
    {
        fs.copyFileSync(integrationDir + "/InstallAgentPackage.xml", directory + "/DistributedTask/Service/Servicing/Host/Deployment/Groups/InstallAgentPackage.xml");
    }
    var newBranch = "users/" + process.env.USER + "/agent-" + release;
    execInForground(git + " add DistributedTask", directory);
    commitAndPush(directory, release, newBranch);

    console.log("Create pull-request for this change ");
    console.log("       https://dev.azure.com/mseng/_git/AzureDevOps/pullrequests?_a=mine");
    console.log("");
}

function commitADOConfigChange(directory, release)
{
    var gitUrl =  "https://mseng@dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps.ConfigChange"

    cloneOrPull(directory, gitUrl);
    var agentVersionPath=release.replace('.', '-');
    var dirs = fs.readdirSync(directory + "/tfs", { withFileTypes: true })
     .filter(dirent => dirent.isDirectory() && dirent.name.startsWith("m"))
     .map(dirent => dirent.name)
     .sort(naturalSort({direction: 'desc'}))
    var milestoneDir = dirs[0];
    var targetDir = "PublishVSTSAgent-" + agentVersionPath;
    if (opt.options.dryrun)
    {
        console.log("Copy file from " + integrationDir + "/" + targetDir + " to " + directory + "/tfs/" + milestoneDir );
    }
    else
    {
        fs.mkdirSync(directory + "/tfs/" + milestoneDir + "/" + targetDir);
        fs.readdirSync(integrationDir + "/" + targetDir).forEach( function (file) {
            fs.copyFileSync(integrationDir + "/" + targetDir + "/" + file, directory + "/tfs/" + milestoneDir + "/" + file);
        });
    }

    var newBranch = "users/" + process.env.USER + "/agent-" + release;
    execInForground(git + " add tfs/" + milestoneDir, directory);
    commitAndPush(directory, release, newBranch);

    console.log("Create pull-request for this change ");
    console.log("       https://dev.azure.com/mseng/AzureDevOps/_git/AzureDevOps.ConfigChange/pullrequests?_a=mine");
    console.log("");
}

function checkGitStatus()
{
    var git_status = cp.execSync(git + ' status --untracked-files=no --porcelain', { encoding: 'utf-8'});
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

async function main(args)
{
    var newRelease = opt.argv[0];
    if (newRelease === undefined)
    {
        console.log('Error: You must supply a version');
        process.exit(-1);
    }
    verifyNewReleaseTagOk(newRelease,
        function() {
            checkGitStatus();
            writeAgentVersionFile(newRelease);
            fetchPRsSinceLastReleaseAndEditReleaseNotes(newRelease, function () {
                createIntegrationFiles(newRelease, function () {
                    commitAgentChanges(__dirname + "/../", newRelease);
                    commitADOL2Changes(integrationDir + "/AzureDevOps", newRelease);
                    commitADOConfigChange(integrationDir + "/AzureDevOps.ConfigChange", newRelease);
                    console.log('done.');
                });
            });
        }
    );
}

main(process.argv.slice(2));