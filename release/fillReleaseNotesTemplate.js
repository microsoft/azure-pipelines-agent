const fs = require('fs');
const path = require('path');
const util = require('./util');

/**
 * @param {*} releaseNotes Release notes template text content
 * @returns Release notes where `<HASH>` is replaced with the provided agents package hash
 */
function addHashesToReleaseNotes(releaseNotes) {
    const hashes = util.getHashes();

    const lines = releaseNotes.split('\n');
    const modifiedLines = lines.map((line) => {
        if (!line.includes('<HASH>')) {
            return line;
        }

        // Package is the second column in the releaseNote.md file, get it's value
        const columns = line.split('|').filter((column) => column.length !== 0);
        const packageColumn = columns[1];
        // Inside package column, we have the package name inside the square brackets
        const packageName = packageColumn.substring(packageColumn.indexOf('[') + 1, packageColumn.indexOf(']'));

        return line.replace('<HASH>', hashes[packageName]);
    });

    return modifiedLines.join('\n');
}

/**
 * @param {string} releaseNotes Release notes template text content
 * @param {string} agentVersion Agent version, e.g. 2.193.0
 * @returns Release notes where `<AGENT_VERSION>` is replaced with the provided agent version
 */
function addAgentVersionToReleaseNotes(releaseNotes, agentVersion) {
    return releaseNotes.replace(/<AGENT_VERSION>/g, agentVersion);
}

/**
 * Takes agent version as the first cmdline argument.
 * 
 * Reads the releaseNote.md template file content and replaces `<AGENT_VERSION>` and `<HASH>` with agent version and package hash respectively.
 */
function main() {
    const agentVersion = process.argv[2];
    if (agentVersion === undefined) {
        throw new Error('Agent version argument must be supplied');
    }

    const releaseNotesPath = path.join(__dirname, '..', 'releaseNote.md');
    const releaseNotes = fs.readFileSync(releaseNotesPath, 'utf-8');

    const releaseNotesWithAgentVersion = addAgentVersionToReleaseNotes(releaseNotes, agentVersion);
    const filledReleaseNotes = addHashesToReleaseNotes(releaseNotesWithAgentVersion);
    fs.writeFileSync(releaseNotesPath, filledReleaseNotes);
}

main();
