const fs = require('node:fs/promises');
const path = require('node:path');
const semver = require('semver');

const sources = {
    releases: 'https://nodejs.org/dist/index.json',
    advisories: 'https://raw.githubusercontent.com/nodejs/security-wg/main/vuln/core/index.json',
    schedule: 'https://raw.githubusercontent.com/nodejs/Release/main/schedule.json'
};

function readPinnedVersions(source) {
    const pins = [];
    const seen = new Set();
    for (const match of source.matchAll(/^[ \t]*(NODE(?:\d+)?(?:_[A-Z0-9]+)*_VERSION)=(.*)$/gm)) {
        const variable = match[1];
        const literal = /^"(\d+\.\d+\.\d+)"[ \t]*\r?$/.exec(match[2]);
        const version = literal?.[1];
        const declaredMajor = /^NODE(\d+)/.exec(variable)?.[1];
        if (seen.has(variable) || !version || !semver.valid(version) ||
            (declaredMajor && Number(declaredMajor) !== semver.major(version))) {
            throw new Error(`Invalid or duplicate Node pin ${variable} in src/Misc/externals.sh.`);
        }
        seen.add(variable);
        pins.push({ variable, major: semver.major(version), version });
    }
    if (pins.length === 0) {
        throw new Error('No Node version pins found in src/Misc/externals.sh.');
    }
    return pins;
}

function isDate(value) {
    return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value) &&
        Number.isFinite(Date.parse(`${value}T00:00:00Z`)) &&
        new Date(`${value}T00:00:00Z`).toISOString().slice(0, 10) === value;
}

function getLifecycle(major, schedule, asOf) {
    const entry = schedule[`v${major}`];
    if (!entry || typeof entry !== 'object' || !isDate(entry.start) || !isDate(entry.end) ||
        entry.start >= entry.end ||
        (entry.lts !== undefined && (!isDate(entry.lts) || entry.lts < entry.start || entry.lts >= entry.end)) ||
        (entry.maintenance !== undefined && (!isDate(entry.maintenance) ||
            entry.maintenance < (entry.lts || entry.start) || entry.maintenance > entry.end))) {
        throw new Error(`Invalid or missing lifecycle schedule for Node ${major}.`);
    }
    let status;
    if (asOf >= entry.end) {
        status = 'end-of-life';
    } else if (asOf < entry.start) {
        status = 'not-released';
    } else if (!entry.lts || asOf < entry.lts) {
        status = 'current';
    } else {
        status = entry.maintenance && asOf >= entry.maintenance ? 'maintenance-lts' : 'active-lts';
    }
    return { status, ltsStart: entry.lts || null, endOfLife: entry.end };
}

function analyze(pins, releases, advisories, schedule, asOf) {
    if (!isDate(asOf)) {
        throw new Error('Invalid monitoring date; expected a UTC YYYY-MM-DD date.');
    }
    if (!schedule || typeof schedule !== 'object' || Array.isArray(schedule) || Object.keys(schedule).length === 0) {
        throw new Error('Invalid or empty Node.js lifecycle schedule.');
    }
    if (!Array.isArray(releases) || releases.length === 0 ||
        releases.some(release => !release || typeof release.version !== 'string' || !semver.valid(release.version))) {
        throw new Error('Invalid Node.js release index.');
    }
    if (!advisories || typeof advisories !== 'object' || Array.isArray(advisories) ||
        Object.keys(advisories).length === 0) {
        throw new Error('Invalid or empty Node.js core advisory index.');
    }
    const normalizedAdvisories = Object.entries(advisories).map(([id, advisory]) => {
        if (!/^\d+$/.test(id) || !advisory || !Array.isArray(advisory.cve) ||
            advisory.cve.some(cve => typeof cve !== 'string' || !/^CVE-\d{4}-\d{4,}$/.test(cve)) ||
            typeof advisory.vulnerable !== 'string' || !advisory.vulnerable.trim() ||
            semver.validRange(advisory.vulnerable) === null ||
            typeof advisory.patched !== 'string' ||
            (advisory.patched !== '' && (!advisory.patched.trim() || semver.validRange(advisory.patched) === null))) {
            throw new Error(`Invalid Node.js core advisory NSWG-COR-${id}.`);
        }
        return { ...advisory, id: `NSWG-COR-${id}`, source: 'working-group' };
    });

    const checked = [];
    const skipped = [];
    for (const pin of pins) {
        const lifecycle = getLifecycle(pin.major, schedule, asOf);
        const result = { variable: pin.variable, major: pin.major, pinnedVersion: pin.version, ...lifecycle };
        if (!['active-lts', 'maintenance-lts'].includes(lifecycle.status)) {
            skipped.push(result);
            continue;
        }
        const lineReleases = releases.filter(release =>
            semver.major(release.version) === pin.major && !semver.prerelease(release.version))
            .sort((left, right) => semver.rcompare(left.version, right.version));
        if (lineReleases.length === 0) {
            throw new Error(`Release index contains no stable Node ${pin.major} releases.`);
        }
        if (lineReleases.some(release => !isDate(release.date) || typeof release.security !== 'boolean')) {
            throw new Error(`Invalid metadata for a Node ${pin.major} release.`);
        }
        const latest = lineReleases[0];
        const latestSecurity = lineReleases.find(release => release.security === true);
        // Upstream vulnerable ranges can include patched versions (for example, "24.x").
        const vulnerabilities = normalizedAdvisories.filter(advisory => advisory.cve.length > 0 &&
            semver.satisfies(pin.version, advisory.vulnerable) &&
            (advisory.patched === '' || !semver.satisfies(pin.version, advisory.patched)));
        checked.push({
            ...result,
            latestVersion: semver.clean(latest.version),
            latestReleaseDate: latest.date,
            latestReleaseIsSecurity: latest.security,
            latestSecurityVersion: latestSecurity ? semver.clean(latestSecurity.version) : null,
            latestSecurityReleaseDate: latestSecurity ? latestSecurity.date : null,
            securityUpdateRequired: latestSecurity ? semver.lt(pin.version, latestSecurity.version) : false,
            outdated: semver.lt(pin.version, latest.version),
            vulnerabilities
        });
    }
    return {
        asOf, checked, skipped, advisoriesChecked: normalizedAdvisories.length,
        securityUpdateRequired: checked.some(pin => pin.securityUpdateRequired)
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
        '# Agent Node CVE monitoring', '', `Checked at: ${report.checkedAt}`, '',
        'Version source: `src/Misc/externals.sh`. Scope: bundled Node versions currently in Active or Maintenance LTS.', ''
    ];
    if (report.error) {
        lines.push('**Check incomplete. Vulnerability status is unknown.**', '', markdownCell(report.error));
    } else {
        lines.push(`Lifecycle date (UTC): ${report.asOf}. Checked ${report.advisoriesChecked} upstream core advisories.`, '');
        if (report.checked.length === 0) {
            lines.push('**No bundled Node versions are currently in LTS. No security comparisons were performed.**');
        } else {
            lines.push(
                '| Pin | LTS status | Bundled | Latest stable | Latest security release | Security update required |',
                '| --- | --- | --- | --- | --- | --- |'
            );
            for (const pin of report.checked) {
                const security = pin.latestSecurityVersion
                    ? `${pin.latestSecurityVersion} (${pin.latestSecurityReleaseDate})` : 'None marked';
                lines.push(`| ${pin.variable} | ${pin.status} | ${pin.pinnedVersion} | ${pin.latestVersion} | ${security} | ${pin.securityUpdateRequired ? 'Yes' : 'No'} |`);
            }
            for (const pin of report.checked) {
                lines.push('', `## ${pin.variable} (Node ${pin.major})`, '');
                if (pin.securityUpdateRequired) {
                    lines.push(`**Security update required:** ${pin.pinnedVersion} is older than ${pin.latestSecurityVersion}, marked \`security: true\`.`, '');
                }
                if (pin.outdated) {
                    lines.push(`Update ${pin.variable} from ${pin.pinnedVersion} to ${pin.latestVersion}.`, '');
                }
                if (pin.vulnerabilities.length === 0) {
                    lines.push(pin.securityUpdateRequired
                        ? 'No matching CVEs are currently listed in the working-group index. This does not clear the newer security release; the check still fails.'
                        : 'No applicable CVEs found for this pin in the working-group advisory index.');
                    continue;
                }
                lines.push('| Advisory | CVEs | Severity | Description | Patched versions |', '| --- | --- | --- | --- | --- |');
                for (const advisory of pin.vulnerabilities) {
                    const url = `https://github.com/nodejs/security-wg/blob/main/vuln/core/${advisory.id.slice('NSWG-COR-'.length)}.json`;
                    lines.push(`| [${advisory.id}](${url}) | ${advisory.cve.join(', ')} | ${markdownCell(advisory.severity || 'Not supplied by upstream')} | ${markdownCell(advisory.description || 'Not supplied by upstream')} | ${markdownCell(advisory.patched || 'No patched version listed')} |`);
                }
            }
        }
        if (report.skipped.length > 0) {
            lines.push('', '## Pins outside current LTS scope', '',
                '| Pin | Bundled | Status | LTS starts | Support ends |', '| --- | --- | --- | --- | --- |');
            for (const pin of report.skipped) {
                lines.push(`| ${pin.variable} | ${pin.pinnedVersion} | ${pin.status} | ${pin.ltsStart || 'Not scheduled'} | ${pin.endOfLife} |`);
            }
        }
    }
    lines.push(
        '', `Release index: ${sources.releases}`, '', `Advisory index: ${sources.advisories}`,
        '', `Lifecycle schedule: ${sources.schedule}`,
        '', 'This checks bundled Node pins, not the monitoring machine runtime or LTS lines the agent does not bundle.',
        'A newer security-flagged release requires an update even if its CVEs have not yet been indexed.',
        'The security flag does not identify individual CVEs or establish exploitability in an agent job.',
        'Coverage depends on upstream lifecycle dates, release flags, and published advisories.',
        ''
    );
    return lines.join('\n');
}

async function monitor({ externalsPath, outputDirectory, fetchImpl = fetch, log = console.log, now = new Date() }) {
    await fs.mkdir(outputDirectory, { recursive: true });
    const report = { checkedAt: now.toISOString(), sources };
    let exitCode;
    try {
        const pins = readPinnedVersions(await fs.readFile(externalsPath, 'utf8'));
        const [releases, advisories, schedule] = await Promise.all([
            fetchJson(sources.releases, fetchImpl),
            fetchJson(sources.advisories, fetchImpl),
            fetchJson(sources.schedule, fetchImpl)
        ]);
        Object.assign(report, analyze(pins, releases, advisories, schedule, report.checkedAt.slice(0, 10)));
        exitCode = report.checked.some(pin => pin.securityUpdateRequired || pin.vulnerabilities.length > 0) ? 1 : 0;
    } catch (error) {
        report.error = error.message;
        exitCode = 2;
    }

    const summaryPath = path.join(outputDirectory, 'node-lts-report.md');
    await Promise.all([
        fs.writeFile(summaryPath, renderMarkdown(report)),
        fs.writeFile(path.join(outputDirectory, 'node-lts-report.json'), `${JSON.stringify(report, null, 2)}\n`)
    ]);
    log('##vso[task.setvariable variable=NodeCveReportReady]true');
    log(`##vso[task.uploadsummary]${escapeLoggingValue(summaryPath)}`);
    if (report.error) {
        log(`##vso[task.logissue type=error]${escapeLoggingValue(`Node LTS check incomplete: ${report.error}`)}`);
    } else {
        if (report.checked.length === 0) {
            log('##vso[task.logissue type=warning]No bundled Node versions are currently in LTS; no security comparisons were performed.');
        }
        for (const pin of report.skipped) {
            log(`Skipped ${pin.variable} (${pin.pinnedVersion}): ${pin.status}.`);
        }
        for (const pin of report.checked) {
            log(`${pin.variable}: bundled Node ${pin.pinnedVersion}; latest stable Node ${pin.major}: ${pin.latestVersion}.`);
            if (pin.outdated) {
                log(`##vso[task.logissue type=warning]${pin.variable} bundles older Node ${pin.pinnedVersion}; latest Node ${pin.major} is ${pin.latestVersion}.`);
            }
            if (pin.securityUpdateRequired) {
                log(`##vso[task.logissue type=error]${pin.variable} bundles Node ${pin.pinnedVersion}, older than Node ${pin.major} security release ${pin.latestSecurityVersion} (${pin.latestSecurityReleaseDate}, security: true). Update src/Misc/externals.sh.`);
            }
            for (const advisory of pin.vulnerabilities) {
                log(`##vso[task.logissue type=error]${escapeLoggingValue(`${advisory.cve.join(', ')} affects ${pin.variable} (${pin.pinnedVersion}). Patched versions: ${advisory.patched || 'none listed'}.`)}`);
            }
        }
    }
    return exitCode;
}

if (require.main === module) {
    const outputDirectory = process.argv[2];
    if (!outputDirectory || process.argv.length !== 3) {
        console.error('Usage: node check-node-lts.js <report-directory>');
        process.exitCode = 2;
    } else {
        monitor({
            externalsPath: path.resolve(__dirname, '..', '..', 'src', 'Misc', 'externals.sh'),
            outputDirectory
        }).then(exitCode => {
            process.exitCode = exitCode;
        }).catch(error => {
            console.error(`##vso[task.logissue type=error]${escapeLoggingValue(`Node LTS monitor failed: ${error.message}`)}`);
            process.exitCode = 2;
        });
    }
}

module.exports = { analyze, readPinnedVersions, fetchJson, monitor };
