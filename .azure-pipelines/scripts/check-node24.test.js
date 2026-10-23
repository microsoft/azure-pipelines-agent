const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { test } = require('node:test');
const { analyze, readPinnedVersion, fetchJson, monitor } = require('./check-node24');

const releases = [
    { version: 'v26.30.0', date: '2026-05-01', security: true },
    { version: 'v24.11.0', date: '2025-11-01', security: false },
    { version: 'v24.9.0', date: '2025-09-01', security: false },
    { version: 'v24.10.0', date: '2025-10-01', security: true },
    { version: 'v24.12.0-rc.1', date: '2025-12-01', security: true }
];

function advisory(overrides = {}) {
    return {
        cve: ['CVE-2025-12345', 'CVE-2025-12346'],
        vulnerable: '20.x || 22.x || 24.x',
        patched: '^20.19.2 || ^22.15.1 || ^24.10.0',
        severity: 'high',
        description: 'Example | vulnerability\nwith multiple lines',
        ref: 'https://nodejs.org/en/blog/vulnerability/example/',
        affectedEnvironments: ['all'],
        ...overrides
    };
}

test('reads only the repository Node 24 pin, including CRLF files', () => {
    assert.equal(readPinnedVersion('NODE20_VERSION="20.20.2"\r\nNODE24_VERSION="24.9.0"\r\n'), '24.9.0');
});

test('rejects missing, duplicate, malformed, prerelease, and wrong-major pins', () => {
    for (const source of [
        '# NODE24_VERSION="24.9.0"',
        'NODE24_VERSION="24.9.0"\nNODE24_VERSION="24.10.0"',
        'NODE24_VERSION="24.bad.0"',
        'NODE24_VERSION="24.10.0-rc.1"',
        'NODE24_VERSION="22.9.0"',
        'NODE24_VERSION="24.9.0'
    ]) {
        assert.throws(() => readPinnedVersion(source), /NODE24_VERSION/);
    }
});

test('finds the newest stable Node 24 security release numerically, not by date or array order', () => {
    const result = analyze('24.9.0', releases, { 1: advisory() });
    assert.equal(result.pinnedVersion, '24.9.0');
    assert.equal(result.latestVersion, '24.11.0');
    assert.equal(result.latestReleaseIsSecurity, false);
    assert.equal(result.latestSecurityVersion, '24.10.0');
    assert.equal(result.latestSecurityReleaseDate, '2025-10-01');
    assert.equal(result.securityUpdateRequired, true);
    assert.equal(result.outdated, true);
});

test('selects the newest of multiple security releases and excludes other majors and prereleases', () => {
    const result = analyze('24.9.0', [
        ...releases,
        { version: 'v24.8.1', date: '2025-12-31', security: true }
    ], { 1: advisory() });
    assert.equal(result.latestSecurityVersion, '24.10.0');
});

test('does not require a security update when the pin equals or exceeds the newest security release', () => {
    for (const version of ['24.10.0', '24.11.0', '24.12.0']) {
        const result = analyze(version, releases, { 1: advisory() });
        assert.equal(result.securityUpdateRequired, false);
        assert.deepEqual(result.vulnerabilities, []);
    }
});

test('handles a release index with no Node 24 security releases without using another major', () => {
    const result = analyze('24.9.0', releases.map(release => ({
        ...release, security: release.version.startsWith('v26.')
    })), { 1: advisory({ patched: '^24.8.0' }) });
    assert.equal(result.latestSecurityVersion, null);
    assert.equal(result.latestSecurityReleaseDate, null);
    assert.equal(result.securityUpdateRequired, false);
    assert.equal(result.outdated, true);
});

test('retains precise CVE ranges and does not manufacture CVEs from a security flag', () => {
    const result = analyze('24.9.0', releases, { 1: advisory() });
    assert.equal(result.advisoriesChecked, 1);
    assert.equal(result.vulnerabilities.length, 1);
    assert.deepEqual(result.vulnerabilities[0].cve, ['CVE-2025-12345', 'CVE-2025-12346']);
    assert.equal(result.vulnerabilities[0].patched, '^20.19.2 || ^22.15.1 || ^24.10.0');
    const missing = analyze('24.9.0', releases, { 1: advisory({ patched: '^24.8.0' }) });
    assert.equal(missing.securityUpdateRequired, true);
    assert.deepEqual(missing.vulnerabilities, []);
});

test('handles bounded, disjoint, and unpatched vulnerability ranges', () => {
    const advisories = {
        1: advisory({ vulnerable: '>=24.2.0 <24.4.0 || >=24.8.0 <24.10.0', patched: '^24.10.0' }),
        2: advisory({ patched: '' })
    };
    assert.equal(analyze('24.9.0', releases, advisories).vulnerabilities.length, 2);
    assert.equal(analyze('24.5.0', releases, advisories).vulnerabilities.length, 1);
});

test('rejects missing Node 24 releases and invalid or empty source data', () => {
    for (const invalid of [null, {}, [], [{ version: 'broken' }], [{ version: 'v26.1.0' }],
        [{ version: 'v24.10.0' }]]) {
        assert.throws(() => analyze('24.9.0', invalid, { 1: advisory() }), /release/i);
    }
    for (const invalid of [null, [], {}, { 1: null }, { 1: advisory({ cve: 'CVE-2025-12345' }) },
        { 1: advisory({ vulnerable: 'broken' }) }, { 1: advisory({ patched: 'broken' }) }]) {
        assert.throws(() => analyze('24.9.0', releases, invalid), /advisor/i);
    }
});

test('rejects missing or non-boolean security flags even on older Node 24 releases', () => {
    for (const security of [undefined, null, 'true', 'false', 0, 1]) {
        const invalid = releases.map(release =>
            release.version === 'v24.10.0' ? { ...release, security } : release);
        assert.throws(() => analyze('24.9.0', invalid, { 1: advisory() }), /release/i);
    }
});

test('fetches JSON with a timeout and rejects HTTP, JSON, and network failures', async () => {
    const url = 'https://example.test/data.json';
    assert.deepEqual(await fetchJson(url, async (requested, options) => {
        assert.equal(requested, url);
        assert.ok(options.signal instanceof AbortSignal);
        return { ok: true, json: async () => ({ result: true }) };
    }), { result: true });
    for (const fetchImpl of [
        async () => ({ ok: false, status: 503 }),
        async () => ({ ok: true, json: async () => { throw new Error('Invalid JSON'); } }),
        async () => { throw new Error('Network unavailable'); }
    ]) {
        await assert.rejects(fetchJson(url, fetchImpl), /503|Invalid JSON|Network unavailable/);
    }
});

async function runMonitor(t, version, advisories, options = {}) {
    const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'agent-node24-test-'));
    t.after(() => fs.rm(directory, { recursive: true, force: true }));
    const externalsPath = path.join(directory, 'externals.sh');
    await fs.writeFile(externalsPath, `NODE24_VERSION="${version}"\n`);
    const outputDirectory = path.join(directory, 'report');
    const logs = [];
    const requests = [];
    const exitCode = await monitor({
        externalsPath,
        outputDirectory,
        log: message => logs.push(message),
        fetchImpl: options.fetchImpl || (async url => {
            requests.push(url);
            if (url === 'https://nodejs.org/dist/index.json') {
                return { ok: true, json: async () => options.releases || releases };
            }
            if (url === 'https://raw.githubusercontent.com/nodejs/security-wg/main/vuln/core/index.json') {
                return { ok: true, json: async () => advisories };
            }
            throw new Error(`Unexpected request (announcements must not be used): ${url}`);
        })
    });
    return { exitCode, logs, requests, outputDirectory };
}

test('publishes a summary and JSON before failing for applicable CVEs', async t => {
    const result = await runMonitor(t, '24.9.0', { 1: advisory() });
    assert.equal(result.exitCode, 1);
    assert.equal(result.requests.length, 2);
    assert.ok(result.logs.some(line => line.includes('type=error') && line.includes('CVE-2025-12345')));
    assert.ok(result.logs.includes('##vso[task.setvariable variable=Node24ReportReady]true'));
    assert.ok(result.logs.some(line => line.startsWith('##vso[task.uploadsummary]')));
    const report = JSON.parse(await fs.readFile(path.join(result.outputDirectory, 'node24-report.json'), 'utf8'));
    assert.equal(report.pinnedVersion, '24.9.0');
    assert.equal(report.latestSecurityVersion, '24.10.0');
    assert.equal(report.vulnerabilities.length, 1);
    assert.ok(report.checkedAt);
    assert.deepEqual(Object.keys(report.sources).sort(), ['advisories', 'releases']);
    const markdown = await fs.readFile(path.join(result.outputDirectory, 'node24-report.md'), 'utf8');
    for (const expected of ['24.9.0', '24.10.0', '24.11.0', 'CVE-2025-12345', '^24.10.0', 'high']) {
        assert.ok(markdown.includes(expected), expected);
    }
    assert.ok(markdown.includes('Example \\| vulnerability with multiple lines'));
});

test('fails for a newer security release even when the CVE index has no matching records', async t => {
    const result = await runMonitor(t, '24.18.1', { 1: advisory({ patched: '^24.10.0' }) }, {
        releases: [
            { version: 'v24.18.1', date: '2026-07-28', security: true },
            { version: 'v24.22.1', date: '2026-10-20', security: true }
        ]
    });
    assert.equal(result.exitCode, 1);
    assert.equal(result.requests.length, 2);
    const report = JSON.parse(await fs.readFile(path.join(result.outputDirectory, 'node24-report.json'), 'utf8'));
    assert.equal(report.securityUpdateRequired, true);
    assert.equal(report.latestSecurityVersion, '24.22.1');
    assert.deepEqual(report.vulnerabilities, []);
    assert.ok(!report.error);
    assert.ok(result.logs.some(line => line.includes('type=error') &&
        line.includes('24.18.1') && line.includes('24.22.1')));
    const markdown = await fs.readFile(path.join(result.outputDirectory, 'node24-report.md'), 'utf8');
    assert.match(markdown, /Security update required/);
    assert.match(markdown, /security: true/);
    assert.doesNotMatch(markdown, /No applicable CVEs/);
});

test('warns but succeeds when only a newer nonsecurity release is available', async t => {
    const result = await runMonitor(t, '24.10.0', { 1: advisory() });
    assert.equal(result.exitCode, 0);
    assert.ok(result.logs.some(line => line.includes('type=warning') && line.includes('24.11.0')));
    assert.ok(!result.logs.some(line => line.includes('type=error')));
});

test('still fails for indexed CVEs even if there is no newer security-flagged release', async t => {
    const result = await runMonitor(t, '24.11.0', { 1: advisory({ patched: '' }) });
    assert.equal(result.exitCode, 1);
    assert.ok(result.logs.some(line => line.includes('type=error') && line.includes('CVE-2025-12345')));
});

test('succeeds without warnings for an up-to-date patched version', async t => {
    const result = await runMonitor(t, '24.11.0', { 1: advisory() });
    assert.equal(result.exitCode, 0);
    assert.ok(!result.logs.some(line => line.includes('task.logissue')));
});

test('fails explicitly and publishes an incomplete summary on data-source failure', async t => {
    const result = await runMonitor(t, '24.9.0', {}, {
        fetchImpl: async () => ({ ok: false, status: 503 })
    });
    assert.equal(result.exitCode, 2);
    assert.ok(result.logs.some(line => line.includes('type=error') && line.includes('503')));
    assert.ok(result.logs.some(line => line.startsWith('##vso[task.uploadsummary]')));
    const markdown = await fs.readFile(path.join(result.outputDirectory, 'node24-report.md'), 'utf8');
    assert.match(markdown, /incomplete/i);
    assert.doesNotMatch(markdown, /No applicable CVEs/);
});
