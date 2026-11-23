const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { test } = require('node:test');

const useWsl = process.platform === 'win32' && process.env.SERVICE_SCRIPT_TEST_WSL === '1';
const bash = useWsl ? 'wsl.exe' : process.platform === 'win32'
    ? path.join(process.env.ProgramFiles, 'Git', 'bin', 'bash.exe')
    : 'bash';
const layoutbin = path.resolve(__dirname, '..', '..', 'src', 'Misc', 'layoutbin');
const read = file => fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
const sourceService = read(path.join(layoutbin, 'runsvc.sh'));
const updateTemplate = read(path.join(layoutbin, 'update.sh.template'));
const history = require('./runsvc-history.json');
const hash = content => crypto.createHash('sha256').update(content).digest('hex');
// Mirrors the StampServiceScript build step in src/dir.proj.
const stamp = content => `${content}# agent-stock-sha256: ${hash(content)}\n`;
const stockService = stamp(sourceService);
const hashes = [...new Set(history.map(item => hash(item.content)))];

function runUpdate({ service, newService = stockService, serviceConfigured = false, symlink = false, preview, omitManifest = false, manifestEol = '\n', setup = '', probeRuntime = false, node24Runtime = 'ok' } = {}) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'agent-upgrade-'));
    try {
        for (const dir of ['bin', 'externals', 'bin.2.0.0', 'externals.2.0.0']) {
            fs.mkdirSync(path.join(root, dir));
        }
        fs.writeFileSync(path.join(root, 'bin', 'old-agent'), 'old');
        fs.writeFileSync(path.join(root, 'externals', 'old-runtime'), 'old');
        fs.writeFileSync(path.join(root, 'bin.2.0.0', 'runsvc.sh'), newService);
        fs.writeFileSync(path.join(root, 'bin.2.0.0', 'Agent.Listener'),
            '#!/bin/bash\nif [ "$1" = "--service-script-telemetry" ]; then\n' +
            '  printf "%s\\n" "$*" > "$(dirname "$0")/telemetry.txt"\n' +
            'else\n  printf "listener-launched:%s\\n" "$*"\nfi\n',
            { mode: 0o755 });
        if (probeRuntime) {
            for (const runtime of ['node24', 'node20_1']) {
                const directory = path.join(root, 'externals.2.0.0', runtime, 'bin');
                fs.mkdirSync(directory, { recursive: true });
                if (probeRuntime === 'native' && runtime === 'node24') {
                    fs.symlinkSync(process.execPath, path.join(directory, 'node'));
                } else {
                    fs.writeFileSync(path.join(directory, 'node'),
                        `#!/bin/bash\nif [ "$1" = "--version" ]; then echo "${runtime}"; else echo "${runtime}-host:$1"; fi\n`,
                        { mode: 0o755 });
                    fs.chmodSync(path.join(directory, 'node'), 0o755);
                }
            }
            if (probeRuntime === 'native') {
                fs.copyFileSync(path.join(layoutbin, 'AgentService.js'), path.join(root, 'bin.2.0.0', 'AgentService.js'));
            }
        }
        const node24 = path.join(root, 'externals.2.0.0', 'node24', 'bin', 'node');
        if (node24Runtime === 'missing') {
            fs.rmSync(path.join(root, 'externals.2.0.0', 'node24'), { recursive: true, force: true });
        } else if (node24Runtime === 'broken' || !probeRuntime) {
            fs.mkdirSync(path.dirname(node24), { recursive: true });
            fs.rmSync(node24, { force: true });
            fs.writeFileSync(node24, node24Runtime === 'broken'
                ? "#!/bin/bash\necho 'node: /lib64/libc.so.6: version GLIBC_2.28 not found' >&2\nexit 1\n"
                : '#!/bin/bash\necho v24.0.0\n',
                { mode: 0o755 });
            fs.chmodSync(node24, 0o755);
        }
        if (!omitManifest) {
            fs.writeFileSync(path.join(root, 'bin.2.0.0', 'runsvc.sh.sha256'), hashes.join(manifestEol) + manifestEol);
        }
        if (service !== undefined) {
            const name = symlink ? 'customer-service.sh' : 'runsvc.sh';
            fs.writeFileSync(path.join(root, name), service, { mode: 0o750 });
            fs.chmodSync(path.join(root, name), 0o750);
            if (symlink) {
                fs.symlinkSync(name, path.join(root, 'runsvc.sh'));
            }
        }
        if (serviceConfigured) {
            fs.writeFileSync(path.join(root, '.service'), 'service');
        }
        if (preview !== undefined) {
            fs.writeFileSync(path.join(root, 'runsvc.sh.new'), preview);
        }

        const script = updateTemplate
            .replaceAll('_PROCESS_ID_', '99999999')
            .replaceAll('_AGENT_PROCESS_NAME_', 'Agent.Listener')
            .replaceAll('_ROOT_FOLDER_', '$ROOT')
            .replaceAll('_EXIST_AGENT_VERSION_', '1.0.0')
            .replaceAll('_DOWNLOAD_AGENT_VERSION_', '2.0.0')
            .replaceAll('_UPDATE_LOG_', '$ROOT/update.log')
            .replaceAll('_RESTART_INTERACTIVE_AGENT_', '0')
            .replace('trap restore_service_script_on_failure EXIT',
                "trap 'restore_service_script_on_failure; wait' EXIT");
        const args = useWsl ? ['-d', 'Ubuntu', '--exec', 'bash', '-s', '--', root] : ['-s', '--', root];
        const rootSetup = 'ROOT="$1"\n' +
            (useWsl ? 'ROOT="$(wslpath -u "$ROOT")"\n' :
                process.platform === 'win32' ? 'ROOT="$(cygpath -u "$ROOT")"\n' : '');
        const result = spawnSync(bash, args, {
            input: rootSetup + 'ping() { :; }\n' + setup + '\n' + script,
            encoding: 'utf8',
            timeout: 30000
        });
        assert.ifError(result.error);
        const serviceResult = probeRuntime && result.status === 0
            ? spawnSync(bash, args, {
                input: rootSetup + 'cd "$ROOT" || exit 1\nexec bash ./runsvc.sh\n',
                encoding: 'utf8',
                timeout: 30000
            })
            : null;
        if (serviceResult) {
            assert.ifError(serviceResult.error);
        }

        const file = name => fs.existsSync(path.join(root, name)) ? read(path.join(root, name)) : null;
        const files = fs.readdirSync(root).sort();
        const backups = files.filter(name => /^runsvc\.sh\.1\.0\.0\.bak/.test(name));
        const actualService = file('runsvc.sh');
        const serviceMode = actualService === null ? null : fs.statSync(path.join(root, 'runsvc.sh')).mode & 0o777;
        return {
            result,
            serviceResult,
            service: actualService,
            serviceIsLink: actualService !== null && fs.lstatSync(path.join(root, 'runsvc.sh')).isSymbolicLink(),
            serviceMode,
            preview: file('runsvc.sh.new'),
            backups: backups.map(file),
            oldBinStillActive: fs.existsSync(path.join(root, 'bin', 'old-agent')),
            oldExternalsStillActive: fs.existsSync(path.join(root, 'externals', 'old-runtime')),
            oldBinSaved: fs.existsSync(path.join(root, 'bin.1.0.0', 'old-agent')),
            oldExternalsSaved: fs.existsSync(path.join(root, 'externals.1.0.0', 'old-runtime')),
            log: file('update.log.failed') || file('update.log.succeed') || file('update.log') || '',
            failed: files.includes('update.log.failed'),
            succeeded: files.includes('update.log.succeed'),
            telemetry: file(path.join('bin.2.0.0', 'telemetry.txt')),
            files
        };
    } finally {
        fs.rmSync(root, { recursive: true, force: true });
    }
}

test('the replacement service script prefers Node 24 for AgentService.js', () => {
    const outcome = runUpdate({ service: history[history.length - 1].content, probeRuntime: true });
    assert.equal(outcome.result.status, 0, outcome.result.stderr);
    assert.equal(outcome.serviceResult.status, 0, outcome.serviceResult.stderr);
    assert.match(outcome.serviceResult.stdout, /node24-host:\.\/bin\/AgentService\.js/);
    assert.doesNotMatch(outcome.serviceResult.stdout, /node20_1-host/);
});

test('the replacement runs AgentService.js with a real Node 24 binary', {
    skip: process.platform === 'win32' || process.versions.node.split('.')[0] !== '24'
}, () => {
    const outcome = runUpdate({ service: history[history.length - 1].content, probeRuntime: 'native' });
    assert.equal(outcome.result.status, 0, outcome.result.stderr);
    assert.equal(outcome.serviceResult.status, 0, outcome.serviceResult.stderr);
    assert.match(outcome.serviceResult.stdout, /v24\.\d+\.\d+/);
    assert.match(outcome.serviceResult.stdout, /Starting Agent listener with startup type: service/);
    assert.match(outcome.serviceResult.stdout, /listener-launched:run --startuptype service/);
});

for (const [name, node24Runtime] of [
    ['cannot run on this machine', 'broken'],
    ['is missing from the downloaded agent', 'missing']
]) {
    test(`blocks the upgrade before any change when Node 24 ${name}`, () => {
        const original = history[history.length - 1].content;
        const outcome = runUpdate({ service: original, serviceConfigured: true, node24Runtime });
        assert.notEqual(outcome.result.status, 0);
        assert.equal(outcome.failed, true);
        assert.equal(outcome.service, original);
        assert.deepEqual(outcome.backups, []);
        assert.equal(outcome.preview, null);
        assert.equal(outcome.oldBinStillActive, true);
        assert.equal(outcome.oldExternalsStillActive, true);
        assert.match(outcome.log, /Node\.js 24 .*cannot run on this machine/);
        assert.match(outcome.result.stderr, /Agent update blocked/);
        assert.match(outcome.telemetry || '', /--service-script-telemetry Node24Unsupported/);
    });
}

test('blocks the upgrade when Node 24 cannot run even for an agent without a service', () => {
    const outcome = runUpdate({ node24Runtime: 'broken' });
    assert.notEqual(outcome.result.status, 0);
    assert.equal(outcome.failed, true);
    assert.equal(outcome.oldBinStillActive, true);
});

test('frozen fingerprint list covers every historical unstamped stock script', () => {
    const manifest = read(path.join(layoutbin, 'runsvc.sh.sha256')).split(/\r?\n/);
    assert.deepEqual(new Set(manifest.filter(line => /^[0-9a-f]{64}$/.test(line))), new Set(hashes));
    assert.ok(hashes.includes(hash(sourceService)), 'the last unstamped stock script must stay recognizable');
    assert.doesNotMatch(sourceService, /agent-stock-sha256/, 'the build stamps runsvc.sh; do not commit a marker');
});

test('recognizes a stamped stock script without a fingerprint list entry', () => {
    const futureStock = stamp(sourceService.replace('# insert anything', '# future stock change\n# insert anything'));
    const nextStock = stamp(sourceService + '# next release\n');
    const outcome = runUpdate({ service: futureStock, newService: nextStock, serviceConfigured: true });
    assert.equal(outcome.result.status, 0, outcome.result.stderr);
    assert.equal(outcome.service, nextStock);
    assert.deepEqual(outcome.backups, [futureStock]);
    assert.match(outcome.telemetry, /--service-script-telemetry Stock/);
});

test('treats an already current stamped script as up to date', () => {
    const outcome = runUpdate({ service: stockService, serviceConfigured: true });
    assert.equal(outcome.result.status, 0, outcome.result.stderr);
    assert.equal(outcome.service, stockService);
    assert.deepEqual(outcome.backups, []);
});

for (const [name, mutate] of [
    ['an edited body', content => content.replace('# insert anything to setup env when running as a service\n',
        '# insert anything to setup env when running as a service\nexport CUSTOMER_SETUP=preserve-me\n')],
    ['lines appended after the marker', content => content + 'export CUSTOMER_SETUP=preserve-me\n'],
    ['a marker without a final newline', content => content.slice(0, -1)],
    ['a marker with CRLF line endings', content => content.replace(/\n/g, '\r\n')],
    ['an uppercase marker hash', content => content.replace(/: ([0-9a-f]{64})\n$/, (_, h) => `: ${h.toUpperCase()}\n`)],
    ['a marker that is not on its own line', content => content.replace(/\n(# agent-stock-sha256: )/, ' $1')]
]) {
    test(`blocks a stamped script with ${name}`, () => {
        const custom = mutate(stockService);
        assert.notEqual(custom, stockService);
        const outcome = runUpdate({ service: custom, newService: stamp(sourceService + '# next release\n'), serviceConfigured: true });
        assert.notEqual(outcome.result.status, 0);
        assert.equal(outcome.service, custom.replace(/\r\n/g, '\n'));
        assert.equal(outcome.oldBinStillActive, true);
        assert.equal(outcome.oldExternalsStillActive, true);
        assert.match(outcome.telemetry, /--service-script-telemetry Unrecognized/);
    });
}

for (const { revision, content } of history) {
    test(`replaces the full stock service script from historical revision ${revision}`, () => {
        const outcome = runUpdate({ service: content, serviceConfigured: true });
        assert.equal(outcome.result.status, 0, outcome.result.stderr);
        assert.equal(outcome.service, stockService);
        assert.equal(outcome.succeeded, true);
        assert.equal(outcome.oldBinSaved, true);
        assert.equal(outcome.oldExternalsSaved, true);
        assert.deepEqual(outcome.backups, content === stockService ? [] : [content]);
        assert.match(outcome.telemetry, /--service-script-telemetry Stock/);
        if (process.platform !== 'win32') {
            assert.equal(outcome.serviceMode, 0o750);
        }
    });
}

test('blocks a customized service script before switching the active agent', () => {
    const custom = history[0].content.replace(
        '# insert anything to setup env when running as a service\n',
        'export CUSTOMER_SETUP=preserve-me\n');
    assert.notEqual(custom, history[0].content);
    const outcome = runUpdate({ service: custom, serviceConfigured: true });
    assert.notEqual(outcome.result.status, 0);
    assert.equal(outcome.service, custom);
    assert.equal(outcome.preview, stockService);
    assert.equal(outcome.oldBinStillActive, true);
    assert.equal(outcome.oldExternalsStillActive, true);
    assert.equal(outcome.failed, true);
    assert.equal(outcome.succeeded, false);
    assert.match(outcome.log, /ERROR.*(custom|unrecognized)/i);
    assert.match(outcome.result.stderr, /ERROR.*(custom|unrecognized)/i);
    assert.match(outcome.telemetry, /--service-script-telemetry Unrecognized/);
    assert.deepEqual(outcome.backups, []);
});

test('does not overwrite an existing preview for a customized script', () => {
    const outcome = runUpdate({ service: '#!/bin/bash\n# customized\n', preview: 'existing preview' });
    assert.notEqual(outcome.result.status, 0);
    assert.equal(outcome.preview, 'existing preview');
    assert.equal(outcome.oldBinStillActive, true);
    assert.equal(outcome.oldExternalsStillActive, true);
});

test('preserves a symlinked service script and blocks the update', t => {
    let outcome;
    try {
        outcome = runUpdate({ service: history[0].content, symlink: true, serviceConfigured: true });
    } catch (error) {
        if (process.platform === 'win32' && ['EPERM', 'EACCES'].includes(error.code)) {
            t.skip('Creating symbolic links is not permitted');
            return;
        }
        throw error;
    }
    assert.notEqual(outcome.result.status, 0);
    assert.equal(outcome.serviceIsLink, true);
    assert.equal(outcome.service, history[0].content);
    assert.equal(outcome.preview, stockService);
    assert.equal(outcome.oldBinStillActive, true);
    assert.equal(outcome.oldExternalsStillActive, true);
    assert.match(outcome.telemetry, /--service-script-telemetry Unrecognized/);
});

test('does not upgrade when the stock fingerprint list is unavailable', () => {
    const outcome = runUpdate({ service: history[0].content, omitManifest: true });
    assert.notEqual(outcome.result.status, 0);
    assert.equal(outcome.service, history[0].content);
    assert.equal(outcome.oldBinStillActive, true);
    assert.equal(outcome.oldExternalsStillActive, true);
    assert.equal(outcome.failed, true);
    assert.match(outcome.log, /ERROR/i);
});

test('recognizes stock scripts when the fingerprint list uses CRLF line endings', () => {
    const outcome = runUpdate({ service: history[history.length - 1].content, manifestEol: '\r\n' });
    assert.equal(outcome.result.status, 0, outcome.result.stderr);
    assert.equal(outcome.service, stockService);
    assert.equal(outcome.succeeded, true);
});

test('uses shasum when sha256sum is unavailable', () => {
    const outcome = runUpdate({
        service: history[history.length - 1].content,
        setup: 'command() { if [ "$1" = "-v" ] && [ "$2" = "sha256sum" ]; then return 1; fi; builtin command "$@"; }\n' +
            'shasum() { [ "$1" = "-a" ] && [ "$2" = "256" ] || return 1; shift 2; command sha256sum "$@"; }'
    });
    assert.equal(outcome.result.status, 0, outcome.result.stderr);
    assert.equal(outcome.service, stockService);
    assert.equal(outcome.succeeded, true);
});

test('blocks a missing script for a configured service', () => {
    const outcome = runUpdate({ serviceConfigured: true });
    assert.notEqual(outcome.result.status, 0);
    assert.equal(outcome.oldBinStillActive, true);
    assert.equal(outcome.oldExternalsStillActive, true);
    assert.equal(outcome.failed, true);
    assert.match(outcome.telemetry, /--service-script-telemetry Missing/);
});

test('upgrades normally when there is no service script or service configuration', () => {
    const outcome = runUpdate();
    assert.equal(outcome.result.status, 0, outcome.result.stderr);
    assert.equal(outcome.service, null);
    assert.equal(outcome.oldBinSaved, true);
    assert.equal(outcome.oldExternalsSaved, true);
    assert.match(outcome.telemetry, /--service-script-telemetry NoService/);
});

test('does not switch the active agent if replacing the service script fails', () => {
    const outcome = runUpdate({
        service: history[history.length - 1].content,
        setup: 'mv() { if [ "${!#}" = "$ROOT/runsvc.sh" ]; then return 1; fi; command mv "$@"; }'
    });
    assert.notEqual(outcome.result.status, 0);
    assert.equal(outcome.service, history[history.length - 1].content);
    assert.equal(outcome.oldBinStillActive, true);
    assert.equal(outcome.oldExternalsStillActive, true);
    assert.equal(outcome.failed, true);
    assert.match(outcome.log, /ERROR/i);
});

test('does not overwrite a service script edited during the upgrade', () => {
    const service = history[history.length - 1].content;
    const change = '\nexport CUSTOMER_SETUP=changed\n';
    const outcome = runUpdate({
        service,
        setup: 'cp() { if [ "$1" = "-a" ] && [ "$2" = "$ROOT/runsvc.sh" ] && [[ "$3" = *.bak.* ]]; then ' +
            'printf "\\nexport CUSTOMER_SETUP=changed\\n" >> "$ROOT/runsvc.sh"; fi; command cp "$@"; }'
    });
    assert.notEqual(outcome.result.status, 0);
    assert.equal(outcome.service, service + change);
    assert.equal(outcome.oldBinStillActive, true);
    assert.equal(outcome.oldExternalsStillActive, true);
    assert.equal(outcome.failed, true);
});

test('restores the original service script if switching the agent subsequently fails', () => {
    const service = history[history.length - 1].content;
    const outcome = runUpdate({
        service,
        setup: 'ln() { if [ "$1" = "-s" ] && [ "${!#}" = "$ROOT/bin" ]; then return 1; fi; command ln "$@"; }'
    });
    assert.notEqual(outcome.result.status, 0);
    assert.equal(outcome.service, service);
    assert.equal(outcome.failed, true);
    assert.match(outcome.log, /Restored .*runsvc\.sh/);
});

test('does not overwrite a new customization while rolling back a failed upgrade', () => {
    const change = '\nexport CUSTOMER_SETUP=changed-during-rollback\n';
    const outcome = runUpdate({
        service: history[history.length - 1].content,
        setup: 'ln() { if [ "$1" = "-s" ] && [ "${!#}" = "$ROOT/bin" ]; then ' +
            'printf "\\nexport CUSTOMER_SETUP=changed-during-rollback\\n" >> "$ROOT/runsvc.sh"; return 1; fi; command ln "$@"; }'
    });
    assert.notEqual(outcome.result.status, 0);
    assert.equal(outcome.service, stockService + change);
    assert.equal(outcome.failed, true);
    assert.match(outcome.log, /ERROR.*restore/i);
});
