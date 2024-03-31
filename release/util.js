const cp = require('child_process');
const fs = require('fs');
const path = require('path');

const GIT = 'git';
const GIT_RELEASE_RE = /([0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3})/;

exports.verifyMinimumNodeVersion = function()
{
    var version = process.version;
    var minimumNodeVersion = '12.10.0'; // this is the version of node that supports the recursive option to rmdir
    if (parseFloat(version.substr(1,version.length)) < parseFloat(minimumNodeVersion))
    {
        console.log('Version of Node does not support recursive directory deletes. Be sure you are starting with a clean workspace!');

    }
    console.log(`Using node version ${version}`);
}

exports.verifyMinimumGitVersion = function()
{
    var gitVersionOutput = cp.execSync(`${GIT} --version`, { encoding: 'utf-8'});
    if (!gitVersionOutput)
    {
        console.log('Unable to get Git Version.');
        process.exit(-1);
    }
    var gitVersion = gitVersionOutput.match(GIT_RELEASE_RE)[0];

    var minimumGitVersion = '2.25.0'; // this is the version that supports sparse-checkout
    if (parseFloat(gitVersion) < parseFloat(minimumGitVersion))
    {
        console.log(`Version of Git does not meet minimum requirement of ${minimumGitVersion}`);
        process.exit(-1);
    }
    console.log(`Using git version ${gitVersion}`);

}

exports.execInForeground = function(command, directory, dryrun = false)
{
    directory = directory || '.';
    console.log(`% ${command}`);
    if (!dryrun)
    {
        cp.execSync(command, { cwd: directory, stdio: [process.stdin, process.stdout, process.stderr] });
    }
}

/**
 * Replaces `<AGENT_VERSION>` and `<HASH_VALUE>` with the right values depending on agent package file name
 * 
 * @param {string} template Template path (e.g. InstallAgentPackage.template.xml or Publish.template.ps1 paths)
 * @param {string} destination Path where the filled template should be written (e.g. InstallAgentPackage.xml path)
 * @param {string} version Agent version, e.g. 2.193.0
 */
exports.fillAgentParameters = function(template, destination, version)
{
    try
    {
        var data = fs.readFileSync(template, 'utf8');
        data = data.replace(/<AGENT_VERSION>/g, version);

        const hashes = exports.getHashes();
        const dataLines = data.split('\n');
        const modifiedDataLines = dataLines.map((line) => {
            for (const packageName of Object.keys(hashes)) {
                if (line.includes(packageName)) {
                    return line.replace('<HASH_VALUE>', hashes[packageName]);
                }
            }

            return line;
        });

        data = modifiedDataLines.join('\n');

        console.log(`Generating ${destination}`);
        fs.writeFileSync(destination, data);
    }
    catch(e)
    {
        console.log('Error:', e.stack);
    }
}

/**
 * @returns A map where the keys are the agent package file names and the values are corresponding packages hashes
 */
exports.getHashes = function() {
    const hashesDirPath = path.join(__dirname, '..', '_hashes', 'hash');
    const hashFiles = fs.readdirSync(hashesDirPath);

    const hashes = {};
    for (const hashFileName of hashFiles) {
        const hashFilePath = path.join(hashesDirPath, hashFileName);
        if (fs.statSync(hashFilePath).isDirectory()) {
            console.log(`Skipping directory ${hashFilePath}`);
           continue;
        }
        const agentPackageFileName = hashFileName.replace('.sha256', '');

        const hashFileContent = fs.readFileSync(hashFilePath, 'utf-8').trim();
        // Last 64 characters are the sha256 hash value
        const hashStringLength = 64;
        const hash = hashFileContent.slice(hashFileContent.length - hashStringLength);

        hashes[agentPackageFileName] = hash;
    }

    return hashes;
}
