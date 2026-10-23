const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const os = require('node:os');
const path = require('node:path');
const { test } = require('node:test');
const { analyze, readPinnedVersions, fetchJson, monitor } = require('./check-node-lts');

const asOf = '2026-10-23';
const schedule = {
    v20: { start: '2023-04-18', lts: '2023-10-24', maintenance: '2024-10-22', end: '2026-04-30' },
    v22: { start: '2024-04-24', lts: '2024-10-29', maintenance: '2025-10-21', end: '2027-04-30' },
    v24: { start: '2025-05-06', lts: '2025-10-28', maintenance: '2026-10-20', end: '2028-04-30' },
    v26: { start: '2026-05-05', lts: '2026-10-28', end: '2029-04-30' },
    v27: { start: '2027-04-22', lts: '2027-10-20', end: '2030-04-30' }
};
const releases = [
    { version: 'v26.12.1', date: asOf, security: true },
    { version: 'v24.23.0', date: asOf, security: false },
    { version: 'v22.23.4', date: asOf, security: true },
    { version: 'v24.9.1', date: asOf, security: true },
    { version: 'v24.18.1', date: asOf, security: false },
    { version: 'v24.22.1', date: asOf, security: true },
    { version: 'v24.24.0-rc.1', date: asOf, security: true },
    { version: 'v20.99.0', date: asOf, security: true, lts: 'Iron' },
    { version: 'v27.1.0', date: asOf, security: true }
];
const advisory = {
    cve: ['CVE-2026-12345'], vulnerable: '20.x || 22.x || 24.x',
    patched: '^20.20.2 || ^22.23.4 || ^24.22.1', severity: 'high',
    description: 'Example | vulnerability\nwith multiple lines'
};
const source = 'NODE20_VERSION="20.20.2"\nNODE22_VERSION="22.21.0"\nNODE24_VERSION="24.18.1"\nNODE26_VERSION="26.1.0"';
const pin = version => `NODE${version.split('.')[0]}_VERSION="${version}"`;
const analyzeSource = (text = source, options = {}) => analyze(readPinnedVersions(text),
    options.releases ?? releases, options.advisories ?? { 1: advisory },
    options.schedule ?? schedule, options.asOf ?? asOf);

test('pin parsing: legacy/platform names and invalid assignments', () => {
    assert.deepEqual(readPinnedVersions('NODE_VERSION="6.17.1"\r\nNODE16_WIN_ARM64_VERSION="16.9.1"\r\nMINGIT_VERSION="2.55.0"'), [
        { variable: 'NODE_VERSION', major: 6, version: '6.17.1' },
        { variable: 'NODE16_WIN_ARM64_VERSION', major: 16, version: '16.9.1' }
    ]);
    for (const text of ['# ' + pin('24.18.1'), pin('24.18.1') + '\n' + pin('24.22.1'),
        pin('24.bad.0'), pin('24.22.1-rc.1'), 'NODE24_VERSION="22.21.0"', 'NODE24_VERSION="24.18.1']) {
        assert.throws(() => readPinnedVersions(text), /pin|NODE24_VERSION/i, text);
    }
});

test('LTS eligibility: all bundled lines, lifecycle boundaries, and no even-major assumption', () => {
    const result = analyzeSource();
    assert.deepEqual(result.checked.map(pin => pin.major), [22, 24]);
    assert.deepEqual(result.skipped.map(pin => pin.status), ['end-of-life', 'current']);
    for (const [version, date, status] of [
        ['20.20.2', '2026-04-29', 'maintenance-lts'], ['20.20.2', '2026-04-30', 'end-of-life'],
        ['24.18.1', '2026-10-19', 'active-lts'], ['24.18.1', '2026-10-20', 'maintenance-lts'],
        ['26.1.0', '2026-10-27', 'current'], ['26.1.0', '2026-10-28', 'active-lts'],
        ['27.0.0', '2027-04-21', 'not-released'], ['27.0.0', '2027-10-20', 'active-lts']
    ]) {
        const { checked, skipped } = analyzeSource(pin(version), { asOf: date });
        assert.equal([...checked, ...skipped][0].status, status, `${version} on ${date}`);
        assert.equal(checked.length, status.endsWith('-lts') ? 1 : 0);
    }
});

test('release and CVE matching: numeric stable selection, patched ranges, and independent platform pins', () => {
    for (const [version, required, cves] of [
        ['24.18.1', true, 1], ['24.22.1', false, 0], ['24.23.0', false, 0], ['24.25.0', false, 0]
    ]) {
        const result = analyzeSource(pin(version)).checked[0];
        assert.deepEqual([result.latestVersion, result.latestSecurityVersion, result.securityUpdateRequired,
            result.vulnerabilities.length], ['24.23.0', '24.22.1', required, cves], version);
    }
    for (const patched of ['', '>=0.0.0', '^22.23.4 || ^24.22.1']) {
        const result = analyzeSource(undefined, { advisories: { 1: {
            ...advisory, vulnerable: '>=22.20.0 <22.22.0 || >=24.18.0 <24.20.0', patched
        } } });
        assert.ok(result.checked.every(pin => pin.vulnerabilities.length === (patched === '>=0.0.0' ? 0 : 1)));
    }
    const noSecurity = analyzeSource(pin('24.18.1'), {
        releases: releases.map(release => ({ ...release, security: !release.version.startsWith('v24.') }))
    }).checked[0];
    assert.deepEqual([noSecurity.latestSecurityVersion, noSecurity.latestSecurityReleaseDate], [null, null]);
    assert.equal(noSecurity.securityUpdateRequired, false);
    let lifecycleReads = 0;
    let releaseChecks = 0;
    const variants = analyzeSource(pin('24.23.0') + '\nNODE24_WIN_ARM64_VERSION="24.18.1"', {
        schedule: { get v24() { lifecycleReads++; return schedule.v24; } },
        releases: releases.map(release => release.version === 'v24.18.1'
            ? { ...release, get date() { releaseChecks++; return release.date; } } : release)
    }).checked;
    assert.deepEqual(variants.map(pin => [pin.securityUpdateRequired, pin.vulnerabilities.length]), [[false, 0], [true, 1]]);
    assert.deepEqual([lifecycleReads, releaseChecks], [1, 1]);
});

test('input failures: invalid upstream data and HTTP/JSON/network errors fail explicitly', async () => {
    const pins = readPinnedVersions(pin('24.18.1'));
    for (const invalid of [null, [], {}, { v24: { ...schedule.v24, end: 'broken' } },
        { v24: { ...schedule.v24, lts: '2025-02-30' } }, { v24: { ...schedule.v24, end: '2025-01-01' } }]) {
        assert.throws(() => analyze(pins, releases, { 1: advisory }, invalid, asOf), /lifecycle|schedule/i);
    }
    assert.throws(() => analyzeSource(pin('28.0.0')), /lifecycle|schedule/i);
    assert.throws(() => analyzeSource(undefined, { asOf: '2026-02-30' }), /date/i);
    for (const invalid of [null, {}, [], [{ version: 'broken' }], [releases[0]],
        ...[undefined, null, 'true', 'false', 0, 1].map(security => [{ ...releases[1], security }])]) {
        assert.throws(() => analyze(pins, invalid, { 1: advisory }, schedule, asOf), /release/i);
    }
    for (const invalid of [null, [], {}, { 1: null }, { 1: { ...advisory, cve: 'invalid' } },
        { 1: { ...advisory, vulnerable: 'broken' } }, { 1: { ...advisory, patched: 'broken' } }]) {
        assert.throws(() => analyze(pins, releases, invalid, schedule, asOf), /advisor/i);
    }
    assert.deepEqual(await fetchJson('https://example.test', async (url, options) => {
        assert.ok(options.signal instanceof AbortSignal);
        return new Response('{"ok":true}');
    }), { ok: true });
    for (const fetchImpl of [async () => new Response('', { status: 503 }),
        async () => new Response('invalid JSON'), async () => { throw new Error('network unavailable'); }]) {
        await assert.rejects(fetchJson('https://example.test', fetchImpl));
    }
});

async function runMonitor(t, options) {
    const directory = await fs.mkdtemp(path.join(os.tmpdir(), 'agent-node-lts-test-'));
    t.after(() => fs.rm(directory, { recursive: true, force: true }));
    const externalsPath = path.join(directory, 'externals.sh');
    await fs.writeFile(externalsPath, options.source ?? source);
    const logs = [];
    const posts = [];
    const feeds = {
        'https://nodejs.org/dist/index.json': releases,
        'https://raw.githubusercontent.com/nodejs/security-wg/main/vuln/core/index.json': options.advisories ?? { 1: advisory },
        'https://raw.githubusercontent.com/nodejs/Release/main/schedule.json': schedule
    };
    const exitCode = await monitor({
        externalsPath, outputDirectory: directory, now: new Date(`${asOf}T12:00:00Z`),
        triggerAlpineBuilds: true, accessToken: options.token === null ? undefined : 'test-token',
        log: message => logs.push(message),
        fetchImpl: async (url, request) => {
            if (Object.hasOwn(feeds, String(url))) {
                assert.equal(request.headers?.Authorization, undefined, 'Credentials must never reach public feeds');
                return new Response(JSON.stringify(feeds[String(url)]), { status: options.sourceError ? 503 : 200 });
            }
            assert.equal(new URL(url).origin, 'https://dev.azure.com');
            if (request.method === 'GET') {
                return new Response(JSON.stringify({ value: options.history ?? [] }), { status: options.historyError ? 403 : 200 });
            }
            const body = JSON.parse(request.body);
            posts.push(body.templateParameters.nodeVersion);
            return options.queue ? options.queue(body) : new Response(JSON.stringify({ id: posts.length, state: 'inProgress' }));
        }
    });
    return {
        exitCode, posts, logs,
        report: JSON.parse(await fs.readFile(path.join(directory, 'node-lts-report.json'), 'utf8')),
        markdown: await fs.readFile(path.join(directory, 'node-lts-report.md'), 'utf8')
    };
}

test('end-to-end: compact summaries, full JSON, exit codes, and preserved security/build failures', async t => {
    const cases = [
        { name: 'security and CVEs', code: 1, posts: ['22.23.4', '24.23.0'] },
        { name: 'security index lag', advisories: { 1: { ...advisory, patched: '>=0.0.0' } }, code: 1, posts: ['22.23.4', '24.23.0'] },
        { name: 'nonsecurity update', source: pin('24.22.1'), code: 0, posts: ['24.23.0'] },
        { name: 'current pin', source: pin('24.23.0'), code: 0, posts: [] },
        { name: 'no LTS pins', source: pin('20.20.2'), token: null, code: 0, posts: [] },
        { name: 'CVE without security update', source: pin('24.23.0'), advisories: { 1: { ...advisory, patched: '' } }, code: 1, posts: [] },
        { name: 'missing token', token: null, code: 2, posts: [] },
        { name: 'source failure', sourceError: true, code: 2, posts: [] },
        { name: 'history failure', historyError: true, code: 2, posts: [] },
        { name: 'existing builds', code: 1, posts: [], history: [
            { id: 8, templateParameters: { nodeVersion: '22.23.4' }, status: 'inProgress', result: null },
            { id: 9, templateParameters: { nodeVersion: '24.23.0' }, status: 'completed', result: 'succeeded' }
        ] },
        { name: 'existing failed builds', code: 1, posts: [], history: [
            { id: 10, templateParameters: { nodeVersion: '22.23.4' }, status: 'completed', result: 'failed' },
            { id: 11, templateParameters: { nodeVersion: '24.23.0' }, status: 'completed', result: 'canceled' }
        ] },
        { name: 'partial queue failure', code: 2, posts: ['22.23.4', '24.23.0'], queue: body =>
            body.templateParameters.nodeVersion === '22.23.4' ? new Response('{"id":15,"state":"inProgress"}')
                : Promise.reject(new Error('Queue | unavailable\n403')) }
    ];
    for (const scenario of cases) {
        const result = await runMonitor(t, scenario);
        assert.equal(result.exitCode, scenario.code, scenario.name);
        assert.deepEqual(result.posts, scenario.posts, scenario.name);
        assert.ok(result.logs.includes('##vso[task.setvariable variable=NodeCveReportReady]true'));
        assert.ok(result.logs.some(line => line.startsWith('##vso[task.uploadsummary]')));
        assert.deepEqual(Object.keys(result.report.sources).sort(), ['advisories', 'releases', 'schedule']);
        assert.match(result.markdown, /Agent Node CVE monitoring/);
        for (const build of result.report.alpineBuilds || []) {
            if (build.outcome !== 'error') {
                assert.ok(result.logs.some(line => line.includes(`Node ${build.nodeVersion}`) &&
                    line.includes(build.url) && line.includes(build.result || build.status)), scenario.name);
            }
        }
        if (result.report.alpineBuilds?.length === 0) {
            assert.ok(result.logs.some(line => line.startsWith('Alpine-NodeJs-Build skipped:')), scenario.name);
        }
        if (scenario.code === 2) {
            assert.ok(result.logs.some(line => line.includes('type=error')), scenario.name);
        }
        if (scenario.name === 'security and CVEs') {
            assert.deepEqual(result.report.checked.map(pin => pin.major), [22, 24]);
            assert.equal(result.report.checked[0].vulnerabilities[0].description, advisory.description);
            assert.match(result.markdown, /CVE-2026-12345/);
            assert.doesNotMatch(result.markdown, /Example.*vulnerability/);
        }
        if (scenario.name === 'no LTS pins') {
            assert.match(result.markdown, /No bundled Node versions are currently in LTS/);
            assert.doesNotMatch(result.markdown, /No applicable CVEs/);
        }
        if (scenario.name === 'source failure') {
            assert.match(result.markdown, /incomplete/i);
        }
        if (scenario.name === 'existing failed builds') {
            assert.deepEqual(result.report.alpineBuilds.map(build => build.outcome), ['existing', 'existing']);
            assert.ok(result.logs.some(line => line.includes('type=warning') && line.includes('Not retrying automatically')));
        }
        if (scenario.name === 'partial queue failure') {
            assert.deepEqual(result.report.alpineBuilds.map(build => build.outcome), ['queued', 'error']);
            assert.match(result.markdown, /buildId=15/);
            assert.match(result.markdown, /Queue \\?\| unavailable 403/);
            assert.ok(result.logs.some(line => line.includes('%0A403')));
            assert.equal(result.report.checked[0].vulnerabilities.length, 1);
        }
    }
});
