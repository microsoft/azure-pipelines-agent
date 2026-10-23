const fs = require('node:fs/promises');
const path = require('node:path');
const semver = require('semver');
const { ensureAlpineBuilds } = require('./alpine-node-build');

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
    const majors = new Map();
    for (const pin of pins) {
        if (!majors.has(pin.major)) {
            const info = getLifecycle(pin.major, schedule, asOf);
            if (['active-lts', 'maintenance-lts'].includes(info.status)) {
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
                const security = lineReleases.find(release => release.security === true);
                Object.assign(info, {
                    latestVersion: semver.clean(latest.version), latestReleaseDate: latest.date,
                    latestReleaseIsSecurity: latest.security,
                    latestSecurityVersion: security ? semver.clean(security.version) : null,
                    latestSecurityReleaseDate: security ? security.date : null
                });
            }
            majors.set(pin.major, info);
        }
        const info = majors.get(pin.major);
        const result = { variable: pin.variable, major: pin.major, pinnedVersion: pin.version, ...info };
        if (!['active-lts', 'maintenance-lts'].includes(info.status)) {
            skipped.push(result);
            continue;
        }
        // Upstream vulnerable ranges can include patched versions (for example, "24.x").
        const vulnerabilities = normalizedAdvisories.filter(advisory => advisory.cve.length > 0 &&
            semver.satisfies(pin.version, advisory.vulnerable) &&
            (advisory.patched === '' || !semver.satisfies(pin.version, advisory.patched)));
        checked.push({
            ...result,
            securityUpdateRequired: info.latestSecurityVersion ? semver.lt(pin.version, info.latestSecurityVersion) : false,
            outdated: semver.lt(pin.version, info.latestVersion),
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

function markdownTable(headers, rows) {
    return [headers, headers.map(() => '---'), ...rows]
        .map(cells => `| ${cells.join(' | ')} |`).join('\n');
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
            lines.push(markdownTable(
                ['Pin', 'LTS status', 'Bundled', 'Latest stable', 'Latest security release', 'Security update required'],
                report.checked.map(pin => [pin.variable, pin.status, pin.pinnedVersion, pin.latestVersion,
                    pin.latestSecurityVersion ? `${pin.latestSecurityVersion} (${pin.latestSecurityReleaseDate})` : 'None marked',
                    pin.securityUpdateRequired ? 'Yes' : 'No'])));
            for (const pin of report.checked) {
                lines.push('', `## ${pin.variable} (Node ${pin.major})`, '');
                lines.push(pin.vulnerabilities.length ? markdownTable(
                    ['Advisory', 'CVEs', 'Severity', 'Description', 'Patched versions'],
                    pin.vulnerabilities.map(advisory => [
                        `[${advisory.id}](https://github.com/nodejs/security-wg/blob/main/vuln/core/${advisory.id.slice('NSWG-COR-'.length)}.json)`,
                        advisory.cve.join(', '), markdownCell(advisory.severity || 'Not supplied by upstream'),
                        markdownCell(advisory.description || 'Not supplied by upstream'),
                        markdownCell(advisory.patched || 'No patched version listed')]))
                    : pin.securityUpdateRequired ? 'No matching CVEs indexed; the newer security release still fails this check.'
                        : 'No applicable CVEs found for this pin in the working-group advisory index.');
            }
        }
        if (report.skipped.length > 0) {
            lines.push('', '## Pins outside current LTS scope', '',
                markdownTable(['Pin', 'Bundled', 'Status', 'LTS starts', 'Support ends'],
                    report.skipped.map(pin => [pin.variable, pin.pinnedVersion, pin.status, pin.ltsStart || 'Not scheduled', pin.endOfLife])));
        }
    }
    if (report.alpineBuilds || report.alpineBuildError) {
        lines.push('', '## Alpine-NodeJs-Build', '',
            'Target: latest stable release in each outdated bundled LTS major. Existing runs are never retried automatically.', '');
        if (report.alpineBuildError) {
            lines.push(`**Alpine build check incomplete:** ${markdownCell(report.alpineBuildError)}`, '');
        }
        if (report.alpineBuilds) {
            lines.push(report.alpineBuilds.length ? markdownTable(['Node version', 'Action', 'Run', 'Status', 'Result'],
                report.alpineBuilds.map(build => build.outcome === 'error'
                    ? [build.nodeVersion, 'Error', markdownCell(build.error), 'Unknown', 'Unknown']
                    : [build.nodeVersion, build.outcome, `[${build.runId}](${build.url})`,
                        markdownCell(build.status), markdownCell(build.result || 'Pending')]))
                : 'No outdated bundled LTS versions require Alpine builds.');
        }
        lines.push('', 'Queuing or finding an Alpine build does not clear security findings or update the agent pins.');
    }
    lines.push(
        '', ...Object.entries(sources).map(([name, url]) => `${name}: ${url}`), '',
        'Coverage depends on upstream lifecycle dates, release flags, and published advisories.',
        'A newer security release requires an update even without indexed CVEs; its flag alone does not establish exploitability.', ''
    );
    return lines.join('\n');
}

async function monitor({
    externalsPath, outputDirectory, fetchImpl = fetch, log = console.log, now = new Date(),
    triggerAlpineBuilds = false, accessToken
}) {
    await fs.mkdir(outputDirectory, { recursive: true });
    const issue = (type, message) => log(`##vso[task.logissue type=${type}]${escapeLoggingValue(message)}`);
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
    if (!report.error && triggerAlpineBuilds) {
        try {
            report.alpineBuilds = await ensureAlpineBuilds(report.checked, { token: accessToken, fetchImpl });
            if (report.alpineBuilds.some(build => build.outcome === 'error')) {
                exitCode = 2;
            }
        } catch (error) {
            report.alpineBuildError = error.message;
            exitCode = 2;
        }
    }

    const summaryPath = path.join(outputDirectory, 'node-lts-report.md');
    await Promise.all([
        fs.writeFile(summaryPath, renderMarkdown(report)),
        fs.writeFile(path.join(outputDirectory, 'node-lts-report.json'), `${JSON.stringify(report, null, 2)}\n`)
    ]);
    log('##vso[task.setvariable variable=NodeCveReportReady]true');
    log(`##vso[task.uploadsummary]${escapeLoggingValue(summaryPath)}`);
    if (report.error) {
        issue('error', `Node LTS check incomplete: ${report.error}`);
    } else {
        if (report.checked.length === 0) {
            issue('warning', 'No bundled Node versions are currently in LTS; no security comparisons were performed.');
        }
        for (const pin of report.skipped) {
            log(`Skipped ${pin.variable} (${pin.pinnedVersion}): ${pin.status}.`);
        }
        for (const pin of report.checked) {
            log(`${pin.variable}: bundled Node ${pin.pinnedVersion}; latest stable Node ${pin.major}: ${pin.latestVersion}.`);
            if (pin.outdated) {
                issue('warning', `${pin.variable} bundles older Node ${pin.pinnedVersion}; latest Node ${pin.major} is ${pin.latestVersion}.`);
            }
            if (pin.securityUpdateRequired) {
                issue('error', `${pin.variable} bundles Node ${pin.pinnedVersion}, older than Node ${pin.major} security release ${pin.latestSecurityVersion} (${pin.latestSecurityReleaseDate}, security: true). Update src/Misc/externals.sh.`);
            }
            for (const advisory of pin.vulnerabilities) {
                issue('error', `${advisory.cve.join(', ')} affects ${pin.variable} (${pin.pinnedVersion}). Patched versions: ${advisory.patched || 'none listed'}.`);
            }
        }
    }
    if (report.alpineBuildError) {
        issue('error', `Alpine build check incomplete: ${report.alpineBuildError}`);
    }
    for (const build of report.alpineBuilds || []) {
        if (build.outcome === 'error') {
            issue('error', `Alpine-NodeJs-Build for Node ${build.nodeVersion}: ${build.error}`);
        } else {
            const message = `Alpine-NodeJs-Build ${build.outcome} for Node ${build.nodeVersion}: ${build.url} (${build.status}, ${build.result || 'pending'}).`;
            if (build.outcome === 'existing' && build.result && build.result !== 'succeeded') {
                issue('warning', `${message} Not retrying automatically.`);
            } else {
                log(message);
            }
        }
    }
    return exitCode;
}

if (require.main === module) {
    const outputDirectory = process.argv[2];
    const triggerAlpineBuilds = process.argv[3] === '--trigger-alpine-builds';
    if (!outputDirectory || (process.argv.length !== 3 && !(process.argv.length === 4 && triggerAlpineBuilds))) {
        console.error('Usage: node check-node-lts.js <report-directory> [--trigger-alpine-builds]');
        process.exitCode = 2;
    } else {
        monitor({
            externalsPath: path.resolve(__dirname, '..', '..', 'src', 'Misc', 'externals.sh'),
            outputDirectory,
            triggerAlpineBuilds,
            accessToken: process.env.SYSTEM_ACCESSTOKEN
        }).then(exitCode => {
            process.exitCode = exitCode;
        }).catch(error => {
            console.error(`##vso[task.logissue type=error]${escapeLoggingValue(`Node LTS monitor failed: ${error.message}`)}`);
            process.exitCode = 2;
        });
    }
}

module.exports = { analyze, readPinnedVersions, fetchJson, monitor };
