const fs = require('node:fs/promises');
const path = require('node:path');
const semver = require('semver');

const sources = {
    releases: 'https://nodejs.org/dist/index.json',
    advisories: 'https://raw.githubusercontent.com/nodejs/security-wg/main/vuln/core/index.json'
};

function readPinnedVersion(source) {
    const assignments = [...source.matchAll(/^[ \t]*NODE24_VERSION=.*$/gm)];
    const match = assignments.length === 1
        ? /^[ \t]*NODE24_VERSION="(24\.\d+\.\d+)"[ \t]*\r?$/.exec(assignments[0][0])
        : null;
    if (!match || !semver.valid(match[1])) {
        throw new Error('Expected exactly one stable Node 24 NODE24_VERSION="24.x.y" pin in src/Misc/externals.sh.');
    }
    return match[1];
}

function analyze(pinnedVersion, releases, advisories) {
    if (!Array.isArray(releases) || releases.length === 0 ||
        releases.some(release => !release || typeof release.version !== 'string' || !semver.valid(release.version))) {
        throw new Error('Invalid Node.js release index.');
    }
    const node24Releases = releases.filter(release =>
        semver.major(release.version) === 24 && !semver.prerelease(release.version));
    node24Releases.sort((left, right) => semver.rcompare(left.version, right.version));
    const latest = node24Releases[0];
    if (!latest) {
        throw new Error('Node.js release index contains no stable Node 24 releases.');
    }
    if (node24Releases.some(release => typeof release.date !== 'string' ||
        !/^\d{4}-\d{2}-\d{2}$/.test(release.date) || typeof release.security !== 'boolean')) {
        throw new Error('Invalid metadata for a Node 24 release.');
    }
    const latestSecurity = node24Releases.find(release => release.security === true);

    if (!advisories || typeof advisories !== 'object' || Array.isArray(advisories) ||
        Object.keys(advisories).length === 0) {
        throw new Error('Invalid or empty Node.js core advisory index.');
    }
    const vulnerabilities = [];
    for (const [id, advisory] of Object.entries(advisories)) {
        if (!/^\d+$/.test(id) || !advisory || !Array.isArray(advisory.cve) ||
            advisory.cve.some(cve => typeof cve !== 'string' || !/^CVE-\d{4}-\d{4,}$/.test(cve)) ||
            typeof advisory.vulnerable !== 'string' || !advisory.vulnerable.trim() ||
            semver.validRange(advisory.vulnerable) === null ||
            typeof advisory.patched !== 'string' ||
            (advisory.patched !== '' && (!advisory.patched.trim() || semver.validRange(advisory.patched) === null))) {
            throw new Error(`Invalid Node.js core advisory NSWG-COR-${id}.`);
        }

        // Upstream vulnerable ranges can include patched versions (for example, "24.x").
        if (advisory.cve.length > 0 && semver.satisfies(pinnedVersion, advisory.vulnerable) &&
            (advisory.patched === '' || !semver.satisfies(pinnedVersion, advisory.patched))) {
            vulnerabilities.push({ ...advisory, id: `NSWG-COR-${id}`, source: 'working-group' });
        }
    }

    return {
        pinnedVersion,
        latestVersion: semver.clean(latest.version),
        latestReleaseDate: latest.date,
        latestReleaseIsSecurity: latest.security,
        latestSecurityVersion: latestSecurity ? semver.clean(latestSecurity.version) : null,
        latestSecurityReleaseDate: latestSecurity ? latestSecurity.date : null,
        securityUpdateRequired: latestSecurity ? semver.lt(pinnedVersion, latestSecurity.version) : false,
        outdated: semver.lt(pinnedVersion, latest.version),
        advisoriesChecked: Object.keys(advisories).length,
        vulnerabilities
    };
}

async function fetchJson(url, fetchImpl = fetch) {
    const response = await fetchImpl(url, { signal: AbortSignal.timeout(60000) });
    if (!response.ok) {
        throw new Error(`Unable to fetch ${url}: HTTP ${response.status}.`);
    }
    return response.json();
}

function escapeLoggingValue(value) {
    return String(value).replace(/%/g, '%AZP25').replace(/\r/g, '%0D').replace(/\n/g, '%0A');
}

function markdownCell(value) {
    return String(value).replace(/[\\`*_[\]<>|]/g, '\\$&').replace(/[\r\n]+/g, ' ');
}

function renderMarkdown(report) {
    const lines = [
        '# Agent Node 24 security report',
        '',
        `Checked at: ${report.checkedAt}`,
        '',
        'Version source: `src/Misc/externals.sh` (`NODE24_VERSION`).',
        ''
    ];
    if (report.error) {
        lines.push('**Check incomplete. Vulnerability status is unknown.**', '', markdownCell(report.error));
    } else {
        lines.push(
            '| Agent pin | Latest stable Node 24 | Latest release date | Outdated |',
            '| --- | --- | --- | --- |',
            `| ${report.pinnedVersion} | ${report.latestVersion} | ${report.latestReleaseDate} | ${report.outdated ? 'Yes' : 'No'} |`,
            '',
            report.latestSecurityVersion
                ? `Latest Node 24 release marked \`security: true\`: **${report.latestSecurityVersion}** (${report.latestSecurityReleaseDate}).`
                : 'No stable Node 24 release is marked `security: true` in the release index.',
            '',
            `Checked ${report.advisoriesChecked} Node.js core advisories.`
        );
        if (report.securityUpdateRequired) {
            lines.push('', `**Security update required:** the agent pin ${report.pinnedVersion} is older than security release ${report.latestSecurityVersion}.`);
        }
        if (report.outdated) {
            lines.push('', `Update NODE24_VERSION from ${report.pinnedVersion} to ${report.latestVersion}.`);
        }
        if (report.vulnerabilities.length === 0) {
            lines.push('', report.securityUpdateRequired
                ? 'No matching CVEs are currently listed in the working-group index. This does not clear the newer security release; the check still fails.'
                : 'No applicable CVEs found in the Node.js security working group advisory index.');
        } else {
            lines.push(
                '', '**Applicable CVEs found for the agent pin.**', '',
                '| Advisory | CVEs | Severity | Description | Patched versions |',
                '| --- | --- | --- | --- | --- |'
            );
            for (const advisory of report.vulnerabilities) {
                const url = `https://github.com/nodejs/security-wg/blob/main/vuln/core/${advisory.id.slice('NSWG-COR-'.length)}.json`;
                lines.push(`| [${advisory.id}](${url}) | ${advisory.cve.join(', ')} | ${markdownCell(advisory.severity || 'Not supplied by upstream')} | ${markdownCell(advisory.description || 'Not supplied by upstream')} | ${markdownCell(advisory.patched || 'No patched version listed')} |`);
            }
        }
    }
    lines.push(
        '', `Release index: ${sources.releases}`,
        '', `Advisory index: ${sources.advisories}`,
        '', 'This checks the Node 24 version bundled from this branch, not the Node runtime on the build machine.',
        'A newer security-flagged release requires an update even if its CVEs have not yet been indexed.',
        'The security flag does not identify individual CVEs or establish exploitability in an agent job.',
        'Coverage depends on upstream release flags and published advisories; a clean result is not a guarantee that no vulnerabilities exist.',
        ''
    );
    return lines.join('\n');
}

async function monitor({ externalsPath, outputDirectory, fetchImpl = fetch, log = console.log }) {
    await fs.mkdir(outputDirectory, { recursive: true });
    const report = { checkedAt: new Date().toISOString(), sources };
    let exitCode;
    try {
        const source = await fs.readFile(externalsPath, 'utf8');
        report.pinnedVersion = readPinnedVersion(source);
        const [releases, advisories] = await Promise.all([
            fetchJson(sources.releases, fetchImpl),
            fetchJson(sources.advisories, fetchImpl)
        ]);
        Object.assign(report, analyze(report.pinnedVersion, releases, advisories));
        exitCode = report.securityUpdateRequired || report.vulnerabilities.length > 0 ? 1 : 0;
    } catch (error) {
        report.error = error.message;
        exitCode = 2;
    }

    const summaryPath = path.join(outputDirectory, 'node24-report.md');
    await Promise.all([
        fs.writeFile(summaryPath, renderMarkdown(report)),
        fs.writeFile(path.join(outputDirectory, 'node24-report.json'), `${JSON.stringify(report, null, 2)}\n`)
    ]);
    log('##vso[task.setvariable variable=Node24ReportReady]true');
    log(`##vso[task.uploadsummary]${escapeLoggingValue(summaryPath)}`);
    if (report.error) {
        log(`##vso[task.logissue type=error]${escapeLoggingValue(`Node 24 check incomplete: ${report.error}`)}`);
    } else {
        log(`Agent Node 24 pin: ${report.pinnedVersion}; latest stable Node 24: ${report.latestVersion}.`);
        if (report.outdated) {
            log(`##vso[task.logissue type=warning]Agent bundles older Node ${report.pinnedVersion}; latest Node 24 is ${report.latestVersion}. Update src/Misc/externals.sh.`);
        }
        if (report.securityUpdateRequired) {
            log(`##vso[task.logissue type=error]Agent bundles Node ${report.pinnedVersion}, older than Node 24 security release ${report.latestSecurityVersion} (${report.latestSecurityReleaseDate}, security: true). Update src/Misc/externals.sh.`);
        }
        for (const advisory of report.vulnerabilities) {
            log(`##vso[task.logissue type=error]${escapeLoggingValue(`${advisory.cve.join(', ')} affects bundled Node ${report.pinnedVersion}. Patched versions: ${advisory.patched || 'none listed'}.`)}`);
        }
    }
    return exitCode;
}

if (require.main === module) {
    const outputDirectory = process.argv[2];
    if (!outputDirectory || process.argv.length !== 3) {
        console.error('Usage: node check-node24.js <report-directory>');
        process.exitCode = 2;
    } else {
        monitor({
            externalsPath: path.resolve(__dirname, '..', '..', 'src', 'Misc', 'externals.sh'),
            outputDirectory
        }).then(exitCode => {
            process.exitCode = exitCode;
        }).catch(error => {
            console.error(`##vso[task.logissue type=error]${escapeLoggingValue(`Node 24 monitor failed: ${error.message}`)}`);
            process.exitCode = 2;
        });
    }
}

module.exports = { analyze, readPinnedVersion, fetchJson, monitor };
