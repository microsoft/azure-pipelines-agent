const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { test } = require('node:test');
const { analyze, readPinnedVersions, fetchJson, monitor } = require('./check-node-lts');

const asOf = '2026-10-23';
const schedule = {
    v6: { start: '2016-04-26', lts: '2016-10-18', end: '2019-04-30' },
    v16: { start: '2021-04-20', lts: '2021-10-26', end: '2023-09-11' },
    v20: { start: '2023-04-18', lts: '2023-10-24', maintenance: '2024-10-22', end: '2026-04-30' },
    v22: { start: '2024-04-24', lts: '2024-10-29', maintenance: '2025-10-21', end: '2027-04-30' },
    v24: { start: '2025-05-06', lts: '2025-10-28', maintenance: '2026-10-20', end: '2028-04-30' },
    v26: { start: '2026-05-05', lts: '2026-10-28', maintenance: '2027-10-20', end: '2029-04-30' },
    v27: { start: '2027-04-22', lts: '2027-10-20', end: '2030-04-30' }
};
const releases = [
    { version: 'v26.12.1', date: '2026-10-21', security: true },
    { version: 'v24.23.0', date: '2026-10-22', security: false },
    { version: 'v22.23.4', date: '2026-10-21', security: true },
    { version: 'v24.18.1', date: '2026-07-28', security: true },
    { version: 'v24.22.1', date: '2026-10-20', security: true },
    { version: 'v22.21.0', date: '2025-10-21', security: false },
    { version: 'v24.24.0-rc.1', date: '2026-10-23', security: true },
    { version: 'v20.99.0', date: '2026-10-23', security: true },
    { version: 'v27.1.0', date: '2027-10-20', security: true }
];
const source = 'NODE20_VERSION="20.20.2"\nNODE22_VERSION="22.21.0"\nNODE24_VERSION="24.18.1"\nNODE26_VERSION="26.1.0"\n';

function advisory(overrides = {}) {
    return {
        cve: ['CVE-2026-12345', 'CVE-2026-12346'],
        vulnerable: '20.x || 22.x || 24.x',
        patched: '^20.20.2 || ^22.23.4 || ^24.22.1',
        severity: 'high',
        description: 'Example | vulnerability\nwith multiple lines',
        ref: 'https://nodejs.org/en/blog/vulnerability/example/',
        ...overrides
    };
}

function analyzeSource(text = source, options = {}) {
    const { releases: releaseData = releases, advisories = { 1: advisory() },
        schedule: lifecycle = schedule, asOf: date = asOf } = options;
    return analyze(readPinnedVersions(text), releaseData, advisories, lifecycle, date);
}

test('discovers every Node pin, including the legacy name, platform variants, and CRLF', () => {
    assert.deepEqual(readPinnedVersions('NODE_VERSION="6.17.1"\r\nNODE16_WIN_ARM64_VERSION="16.9.1"\r\nNODE24_VERSION="24.18.1"\r\nMINGIT_VERSION="2.55.0"\r\n'), [
        { variable: 'NODE_VERSION', major: 6, version: '6.17.1' },
        { variable: 'NODE16_WIN_ARM64_VERSION', major: 16, version: '16.9.1' },
        { variable: 'NODE24_VERSION', major: 24, version: '24.18.1' }
    ]);
});

test('rejects absent, duplicate, malformed, prerelease, and wrong-major pins', () => {
    for (const text of [
        '# NODE24_VERSION="24.18.1"',
        'NODE24_VERSION="24.18.1"\nNODE24_VERSION="24.22.1"',
        'NODE24_VERSION="24.bad.0"',
        'NODE24_VERSION="24.22.1-rc.1"',
        'NODE24_VERSION="22.21.0"',
        'NODE24_VERSION="24.18.1'
    ]) {
        assert.throws(() => readPinnedVersions(text), /Node.*pin|NODE24_VERSION/i);
    }
});

test('checks every bundled supported LTS line and explicitly skips EOL and not-yet-LTS lines', () => {
    const result = analyzeSource();
    assert.deepEqual(result.checked.map(pin => pin.major), [22, 24]);
    assert.deepEqual(result.skipped.map(pin => [pin.major, pin.status]), [
        [20, 'end-of-life'], [26, 'current']
    ]);
    assert.equal(result.checked[0].latestSecurityVersion, '22.23.4');
    assert.equal(result.checked[1].latestSecurityVersion, '24.22.1');
    assert.ok(result.checked.every(pin => pin.securityUpdateRequired));
});

test('includes Active LTS and Maintenance LTS rather than only the newest major', () => {
    const result = analyzeSource(undefined, { asOf: '2026-10-19' });
    assert.deepEqual(result.checked.map(pin => [pin.major, pin.status]), [
        [22, 'maintenance-lts'], [24, 'active-lts']
    ]);
});

test('uses lifecycle dates rather than historical lts labels in the release index', () => {
    const result = analyzeSource('NODE20_VERSION="20.20.2"', {
        releases: [{ version: 'v20.99.0', date: '2026-10-23', security: true, lts: 'Iron' }]
    });
    assert.deepEqual(result.checked, []);
    assert.equal(result.skipped[0].status, 'end-of-life');
});

test('automatically starts checking a bundled major on its LTS start date', () => {
    assert.deepEqual(analyzeSource('NODE26_VERSION="26.1.0"').checked, []);
    const result = analyzeSource('NODE26_VERSION="26.1.0"', { asOf: '2026-10-28' });
    assert.equal(result.checked[0].major, 26);
    assert.equal(result.checked[0].latestSecurityVersion, '26.12.1');
});

test('stops checking a line on its end date and does not assume only even majors can enter LTS', () => {
    assert.equal(analyzeSource('NODE22_VERSION="22.21.0"', { asOf: '2027-04-29' }).checked.length, 1);
    assert.equal(analyzeSource('NODE22_VERSION="22.21.0"', { asOf: '2027-04-30' }).checked.length, 0);
    assert.equal(analyzeSource('NODE27_VERSION="27.0.0"', { asOf: '2027-10-20' }).checked[0].major, 27);
});

test('does not check supported upstream lines that are not bundled by the agent', () => {
    assert.deepEqual(analyzeSource('NODE24_VERSION="24.18.1"').checked.map(pin => pin.major), [24]);
});

test('checks platform-specific pins independently within the same LTS major', () => {
    const result = analyzeSource('NODE22_VERSION="22.23.4"\nNODE22_WIN_ARM64_VERSION="22.21.0"');
    assert.deepEqual(result.checked.map(pin => pin.securityUpdateRequired), [false, true]);
});

test('selects the newest stable security release numerically within each major', () => {
    const result = analyzeSource('NODE24_VERSION="24.18.1"', {
        releases: [...releases, { version: 'v24.9.1', date: '2026-10-23', security: true }]
    }).checked[0];
    assert.equal(result.latestVersion, '24.23.0');
    assert.equal(result.latestReleaseIsSecurity, false);
    assert.equal(result.latestSecurityVersion, '24.22.1');
});

test('does not require a security update when a pin equals or exceeds its latest security release', () => {
    for (const version of ['24.22.1', '24.23.0', '24.25.0']) {
        const result = analyzeSource(`NODE24_VERSION="${version}"`).checked[0];
        assert.equal(result.securityUpdateRequired, false);
        assert.deepEqual(result.vulnerabilities, []);
    }
});

test('handles a supported line with no security-flagged releases without borrowing another major', () => {
    const result = analyzeSource('NODE24_VERSION="24.23.0"', {
        releases: releases.map(release => ({
            ...release, security: !release.version.startsWith('v24.')
        }))
    }).checked[0];
    assert.equal(result.latestSecurityVersion, null);
    assert.equal(result.latestSecurityReleaseDate, null);
    assert.equal(result.securityUpdateRequired, false);
});

test('retains precise CVE ranges and never invents CVEs from a release flag', () => {
    const result = analyzeSource('NODE22_VERSION="22.21.0"\nNODE24_VERSION="24.18.1"');
    assert.ok(result.checked.every(pin => pin.vulnerabilities.length === 1));
    const missing = analyzeSource(undefined, { advisories: { 1: advisory({ patched: '>=0.0.0' }) } });
    assert.ok(missing.checked.every(pin => pin.vulnerabilities.length === 0 && pin.securityUpdateRequired));
});

test('handles bounded, disjoint, and unpatched CVE ranges for every checked pin', () => {
    const result = analyzeSource(undefined, {
        advisories: {
            1: advisory({ vulnerable: '>=22.20.0 <22.22.0 || >=24.18.0 <24.20.0', patched: '' }),
            2: advisory({ vulnerable: '26.x', patched: '' })
        }
    });
    assert.ok(result.checked.every(pin => pin.vulnerabilities.length === 1));
});

test('rejects missing, malformed, or unknown lifecycle data and invalid dates', () => {
    for (const invalid of [null, [], {}, { v24: { ...schedule.v24, end: 'broken' } },
        { v24: { ...schedule.v24, lts: '2025-02-30' } },
        { v24: { ...schedule.v24, end: '2025-01-01' } }]) {
        assert.throws(() => analyzeSource('NODE24_VERSION="24.18.1"', { schedule: invalid }), /schedule|lifecycle/i);
    }
    assert.throws(() => analyzeSource('NODE28_VERSION="28.0.0"'), /schedule|lifecycle/i);
    assert.throws(() => analyzeSource(undefined, { asOf: '2026-02-30' }), /date/i);
});

test('rejects invalid or missing release and advisory data for supported bundled majors', () => {
    for (const invalid of [null, {}, [], [{ version: 'broken' }],
        [{ version: 'v26.12.1', date: asOf, security: true }]]) {
        assert.throws(() => analyzeSource(undefined, { releases: invalid }), /release/i);
    }
    for (const security of [undefined, null, 'true', 'false', 0, 1]) {
        const invalid = releases.map(release =>
            release.version === 'v22.21.0' ? { ...release, security } : release);
        assert.throws(() => analyzeSource(undefined, { releases: invalid }), /release/i);
    }
    for (const invalid of [null, [], {}, { 1: null }, { 1: advisory({ cve: 'CVE-2026-12345' }) },
        { 1: advisory({ vulnerable: 'broken' }) }, { 1: advisory({ patched: 'broken' }) }]) {
        assert.throws(() => analyzeSource(undefined, { advisories: invalid }), /advisor/i);
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

async function runMonitor(t, options = {}) {
    const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'agent-node-lts-test-'));
    t.after(() => fs.rm(directory, { recursive: true, force: true }));
    const externalsPath = path.join(directory, 'externals.sh');
    await fs.writeFile(externalsPath, options.source || source);
    const outputDirectory = path.join(directory, 'report');
    const logs = [];
    const requests = [];
    const exitCode = await monitor({
        externalsPath, outputDirectory, now: new Date(`${asOf}T12:00:00Z`),
        log: message => logs.push(message),
        fetchImpl: options.fetchImpl || (async url => {
            requests.push(url);
            const data = {
                'https://nodejs.org/dist/index.json': options.releases || releases,
                'https://raw.githubusercontent.com/nodejs/security-wg/main/vuln/core/index.json': options.advisories || { 1: advisory() },
                'https://raw.githubusercontent.com/nodejs/Release/main/schedule.json': options.schedule || schedule
            };
            assert.ok(Object.hasOwn(data, url), `Unexpected request: ${url}`);
            return { ok: true, json: async () => data[url] };
        })
    });
    return { exitCode, logs, requests, outputDirectory };
}

test('publishes per-pin Markdown and JSON reports and failures for every vulnerable LTS line', async t => {
    const result = await runMonitor(t);
    assert.equal(result.exitCode, 1);
    assert.equal(result.requests.length, 3);
    assert.ok(result.logs.includes('##vso[task.setvariable variable=NodeCveReportReady]true'));
    assert.ok(result.logs.some(line => line.startsWith('##vso[task.uploadsummary]')));
    const report = JSON.parse(await fs.readFile(path.join(result.outputDirectory, 'node-lts-report.json'), 'utf8'));
    assert.equal(report.asOf, asOf);
    assert.deepEqual(report.checked.map(pin => pin.major), [22, 24]);
    assert.deepEqual(Object.keys(report.sources).sort(), ['advisories', 'releases', 'schedule']);
    const markdown = await fs.readFile(path.join(result.outputDirectory, 'node-lts-report.md'), 'utf8');
    for (const expected of ['NODE22_VERSION', '22.23.4', 'NODE24_VERSION', '24.22.1', 'NODE20_VERSION',
        'end-of-life', 'NODE26_VERSION', 'current', 'CVE-2026-12345', 'Example \\| vulnerability with multiple lines']) {
        assert.ok(markdown.includes(expected), expected);
    }
    for (const version of ['22.21.0', '24.18.1']) {
        assert.ok(result.logs.some(line => line.includes('type=error') && line.includes(version)));
    }
});

test('fails for security releases without matching CVEs and never fails for skipped EOL pins', async t => {
    const result = await runMonitor(t, { advisories: { 1: advisory({ patched: '>=0.0.0' }) } });
    assert.equal(result.exitCode, 1);
    assert.ok(result.logs.some(line => line.includes('type=error') && line.includes('22.23.4')));
    assert.ok(result.logs.some(line => line.includes('type=error') && line.includes('24.22.1')));
    assert.ok(!result.logs.some(line => line.includes('type=error') && line.includes('20.99.0')));
});

test('warns but succeeds when only a newer nonsecurity release is available', async t => {
    const result = await runMonitor(t, { source: 'NODE24_VERSION="24.22.1"' });
    assert.equal(result.exitCode, 0);
    assert.ok(result.logs.some(line => line.includes('type=warning') && line.includes('24.23.0')));
    assert.ok(!result.logs.some(line => line.includes('type=error')));
});

test('fails for indexed CVEs even when there is no newer security-flagged release', async t => {
    const result = await runMonitor(t, {
        source: 'NODE22_VERSION="22.23.4"', advisories: { 1: advisory({ patched: '' }) }
    });
    assert.equal(result.exitCode, 1);
    assert.ok(result.logs.some(line => line.includes('type=error') && line.includes('CVE-2026-12345')));
});

test('reports explicitly when no bundled pins are currently LTS, without claiming a clean vulnerability result', async t => {
    const result = await runMonitor(t, { source: 'NODE20_VERSION="20.20.2"' });
    assert.equal(result.exitCode, 0);
    assert.ok(result.logs.some(line => line.includes('type=warning') && /No bundled Node versions/i.test(line)));
    const markdown = await fs.readFile(path.join(result.outputDirectory, 'node-lts-report.md'), 'utf8');
    assert.match(markdown, /No bundled Node versions are currently in LTS/);
    assert.doesNotMatch(markdown, /No applicable CVEs/);
});

test('fails explicitly with an incomplete report when any source cannot be fetched', async t => {
    const result = await runMonitor(t, { fetchImpl: async () => ({ ok: false, status: 503 }) });
    assert.equal(result.exitCode, 2);
    assert.ok(result.logs.some(line => line.includes('type=error') && line.includes('503')));
    const markdown = await fs.readFile(path.join(result.outputDirectory, 'node-lts-report.md'), 'utf8');
    assert.match(markdown, /incomplete/i);
    assert.doesNotMatch(markdown, /No applicable CVEs/);
});
