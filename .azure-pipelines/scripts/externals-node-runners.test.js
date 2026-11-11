const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { spawnSync } = require('node:child_process');
const { test } = require('node:test');
const { readPinnedVersions } = require('./check-node-lts');

const source = fs.readFileSync(path.resolve(__dirname, '../../src/Misc/externals.sh'), 'utf8');
const bash = process.platform === 'win32'
    ? path.join(process.env.ProgramFiles, 'Git', 'bin', 'bash.exe')
    : 'bash';

test('external runtime pins contain only the bundled Node versions', () => {
    assert.deepEqual(readPinnedVersions(source).map(pin => pin.major).sort((a, b) => a - b), [20, 24]);
});

for (const packageType of ['agent', 'pipelines-agent']) {
    for (const platform of process.platform === 'win32' ? ['linux', 'windows'] : ['linux']) {
        test(`${packageType} ${platform} archive handles a reused layout containing Node20`, () => {
            const root = fs.mkdtempSync(path.join(os.tmpdir(), 'agent-package-'));
            try {
                for (const folder of ['bin', 'externals/node20_1/bin', 'externals/node24/bin']) {
                    fs.mkdirSync(path.join(root, 'layout', folder), { recursive: true });
                }
                fs.writeFileSync(path.join(root, 'layout/bin/Agent.Listener'), '#!/bin/sh\necho 1.0.0\n', { mode: 0o755 });
                fs.writeFileSync(path.join(root, 'layout/externals/node20_1/bin/node'), 'node20');
                fs.writeFileSync(path.join(root, 'layout/externals/node24/bin/node'), 'node24');
                const devSource = fs.readFileSync(path.resolve(__dirname, '../../src/dev.sh'), 'utf8');
                const start = devSource.indexOf('function cmd_package() {');
                const end = devSource.indexOf('function cmd_hash() {', start);
                assert.ok(start >= 0 && end > start);
                const result = spawnSync(bash, ['-s', '--', root], {
                    input: 'set -eo pipefail\nROOT="$1"\n' +
                        (process.platform === 'win32' ? 'ROOT="$(cygpath -am "$ROOT")"\nROOT="/${ROOT:0:1}${ROOT:2}"\n' : '') +
                        'LAYOUT_DIR="$ROOT/layout"\nPACKAGE_DIR="$ROOT/package"\n' +
                        `AGENT_VERSION=1.0.0\nRUNTIME_ID=${platform === 'windows' ? 'win-x64' : 'linux-x64'}\nCURRENT_PLATFORM=${platform}\n` +
                        'function heading() { :; }\n' + devSource.slice(start, end) +
                        '\ncmd_package\n' + (platform === 'linux' ? 'tar -tf "$PACKAGE_DIR"/*.tar.gz\n' : ''),
                    encoding: 'utf8',
                    timeout: 30000,
                    env: { ...process.env, PACKAGE_TYPE: packageType }
                });
                assert.ifError(result.error);
                assert.equal(result.status, 0, result.stderr);
                assert.ok(fs.existsSync(path.join(root, 'layout', 'externals', 'node20_1', 'bin', 'node')),
                    'packaging must not remove Node20 from the shared layout');
                let entries = result.stdout;
                if (platform === 'windows') {
                    const archiveName = packageType === 'agent' ? 'vsts-agent' : packageType;
                    const archivePath = path.join(root, 'package', `${archiveName}-win-x64-1.0.0.zip`).replace(/'/g, "''");
                    const listing = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command',
                        `$ErrorActionPreference = 'Stop'; Add-Type -AssemblyName System.IO.Compression.FileSystem; $zip = [IO.Compression.ZipFile]::OpenRead('${archivePath}'); ` +
                        'try { $zip.Entries | ForEach-Object { $_.FullName } } finally { $zip.Dispose() }'
                    ], { encoding: 'utf8', timeout: 30000 });
                    assert.ifError(listing.error);
                    assert.equal(listing.status, 0, listing.stderr);
                    assert.notEqual(listing.stdout.trim(), '', listing.stderr || result.stdout);
                    entries = listing.stdout.replace(/\\/g, '/');
                }
                assert.match(entries, /externals\/node24\/bin\/node/);
                if (packageType === 'agent') {
                    assert.match(entries, /externals\/node20_1\/bin\/node/);
                } else {
                    assert.doesNotMatch(entries, /node20_1/);
                }
            } finally {
                fs.rmSync(root, { recursive: true, force: true });
            }
        });
    }
    for (const runtime of ['win-x64', 'win-x86', 'win-arm64', 'linux-x64', 'linux-arm',
        'linux-arm64', 'linux-musl-x64', 'linux-musl-arm64', 'osx-x64', 'osx-arm64']) {
        test(`${packageType} external acquisition includes only supported runners on ${runtime}`, () => {
            // Keep the real platform branches, replacing only filesystem/network operations.
            const start = source.indexOf('function acquireExternalTool() {');
            const end = source.indexOf('echo "PACKAGE RUNTIME:');
            assert.ok(start >= 0 && end > start);
            const instrumented = (source.slice(0, start) +
                'function acquireExternalTool() { printf "TOOL:%s\\n" "$2"; }\n\n' +
                source.slice(end))
                .replace(/get_abs_path\(\) \{[\s\S]*?\n\}/,
                    'get_abs_path() { printf "%s\\n" "$1"; }');
            const result = spawnSync(bash, ['-s', '--', runtime], {
                input: 'function rm() { :; }\nfunction cp() { :; }\n' + instrumented,
                encoding: 'utf8',
                timeout: 30000,
                env: { ...process.env, PACKAGE_TYPE: packageType, INCLUDE_NODE20: 'true', INCLUDE_NODE24: 'true' }
            });
            assert.ifError(result.error);
            assert.equal(result.status, 0, result.stderr);
            const runners = result.stdout.split(/\r?\n/)
                .filter(line => /^TOOL:node/.test(line))
                .map(line => line.slice(5).split('/')[0]);
            const expected = packageType === 'agent' ? ['node20_1'] : [];
            if (!['win-x86', 'linux-arm'].includes(runtime)) expected.push('node24');
            assert.deepEqual([...new Set(runners)].sort(), expected, result.stdout);
        });
    }
}

const serviceSource = fs.readFileSync(path.resolve(__dirname, '../../src/Misc/layoutbin/runsvc.sh'), 'utf8');
for (const scenario of [
    { node24: 0, node20: 0, runner: 'node24' },
    { node24: 1, node20: 0, runner: 'node20_1' },
    { node24: 1, node20: 1, runner: null }
]) {
    test(`service runtime selection: ${scenario.runner || 'no compatible bundled runner'}`, () => {
        const instrumented = serviceSource
            .replace('./externals/node24/bin/node', 'probeNode24')
            .replace('./externals/node20_1/bin/node', 'probeNode20')
            .replace('./externals/"$NODE_VER"/bin/node ./bin/AgentService.js &',
                'printf "SERVICE_RUNNER:%s\\n" "$NODE_VER" &');
        const result = spawnSync(bash, ['-s'], {
            input: `function probeNode24() { return ${scenario.node24}; }\n` +
                `function probeNode20() { return ${scenario.node20}; }\n` + instrumented,
            encoding: 'utf8',
            timeout: 30000
        });
        assert.ifError(result.error);
        assert.equal(result.status, scenario.runner ? 0 : 1, result.stderr);
        if (scenario.runner) {
            assert.match(result.stdout, new RegExp(`SERVICE_RUNNER:${scenario.runner}`));
        } else {
            assert.match(result.stderr, /No compatible Node.js runtime found/);
            assert.doesNotMatch(result.stdout, /SERVICE_RUNNER:/);
        }
    });
}
