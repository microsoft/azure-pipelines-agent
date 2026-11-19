const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const { spawnSync } = require('node:child_process');
const { test } = require('node:test');

const bash = process.platform === 'win32'
    ? path.join(process.env.ProgramFiles, 'Git', 'bin', 'bash.exe')
    : 'bash';
const layoutbin = path.resolve(__dirname, '../../src/Misc/layoutbin');
const read = file => fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');
const stockService = read(path.join(layoutbin, 'runsvc.sh'));
const updateTemplate = read(path.join(layoutbin, 'update.sh.template'));

const BEGIN = '# BEGIN AGENT SERVICE NODE SELECTION';
const END = '# END AGENT SERVICE NODE SELECTION';
const LAUNCH = /^\.\/externals\/.*\/bin\/node \.\/bin\/AgentService\.js &$/gm;

function managedRegion(content) {
    const start = content.indexOf(BEGIN);
    const end = content.indexOf('\n', content.indexOf(END));
    assert.ok(start >= 0 && end > start, 'managed region markers not found');
    return content.slice(start, end + 1);
}

function runRefresh(rootService) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'agent-refresh-'));
    try {
        fs.mkdirSync(path.join(root, 'bin'));
        fs.writeFileSync(path.join(root, 'bin', 'runsvc.sh'), stockService, { mode: 0o755 });
        if (rootService !== undefined) {
            fs.writeFileSync(path.join(root, 'runsvc.sh'), rootService, { mode: 0o755 });
        }
        const start = updateTemplate.indexOf('refresh_service_script() {');
        const end = updateTemplate.indexOf('\n}\n', start);
        assert.ok(start >= 0 && end > start, 'refresh_service_script not found in update.sh.template');
        const result = spawnSync(bash, ['-s', '--', root], {
            input: 'ROOT="$1"\n' +
                (process.platform === 'win32' ? 'ROOT="$(cygpath -u "$ROOT")"\n' : '') +
                updateTemplate.slice(start, end + 3) +
                'refresh_service_script "$ROOT" 1.0.0 "$ROOT/update.log"\necho "EXIT:$?"\n',
            encoding: 'utf8',
            timeout: 30000
        });
        assert.ifError(result.error);
        const file = name => fs.existsSync(path.join(root, name)) ? read(path.join(root, name)) : null;
        return {
            result,
            service: file('runsvc.sh'),
            backup: file('runsvc.sh.1.0.0.bak'),
            log: file('update.log') || '',
            files: fs.readdirSync(root).sort()
        };
    } finally {
        fs.rmSync(root, { recursive: true, force: true });
    }
}

function selectedRunner(service, node24, node20) {
    const instrumented = service
        .replace('./externals/node24/bin/node', 'probeNode24')
        .replace('./externals/node20_1/bin/node', 'probeNode20')
        .replace(/\.\/externals\/"\$NODE_VER"\/bin\/node \.\/bin\/AgentService\.js( &)?/,
            (_, background) => 'printf "SERVICE_RUNNER:%s\\n" "$NODE_VER"' + (background || ''));
    const result = spawnSync(bash, ['-s'], {
        input: `function probeNode24() { return ${node24}; }\n` +
            `function probeNode20() { return ${node20}; }\n` + instrumented,
        encoding: 'utf8',
        timeout: 30000
    });
    assert.ifError(result.error);
    return (result.stdout.match(/SERVICE_RUNNER:(\S+)/) || [])[1];
}

test('stock runsvc.sh wraps node selection in a managed region', () => {
    const region = managedRegion(stockService);
    assert.equal(region.match(LAUNCH).length, 1);
    assert.equal(stockService.match(LAUNCH).length, 1);
});

// Exact file snapshots from git log --follow, including every distinct script in release-tag history.
const history = require('./runsvc-history.json');
for (const { revision, content } of history) {
    test(`upgrade preserves service behavior from historical revision ${revision}`, () => {
        const customization = 'export CUSTOM_ENV=keep-me\n';
        const legacy = content.replace('# insert anything to setup env when running as a service\n',
            '# insert anything to setup env when running as a service\n' + customization);
        const { result, service, backup } = runRefresh(legacy);
        assert.match(result.stdout, /EXIT:0/, result.stderr);
        assert.equal(backup, legacy);

        const selectionStart = legacy.search(/^# (fallback on Node16|Prefer Node24|run the host process)/m);
        const launch = /^\.\/externals\/[^\n]+\/bin\/node \.\/bin\/AgentService\.js( &)?\n/m.exec(legacy);
        assert.ok(selectionStart >= 0 && launch);
        const region = launch[1] ? managedRegion(stockService)
            : managedRegion(stockService).replace('AgentService.js &\n', 'AgentService.js\n');
        assert.equal(service, legacy.slice(0, selectionStart) + region +
            legacy.slice(launch.index + launch[0].length));
        const syntax = spawnSync(bash, ['-n'], { input: service, encoding: 'utf8' });
        assert.ifError(syntax.error);
        assert.equal(syntax.status, 0, syntax.stderr);
        assert.equal(selectedRunner(service, 0, 0), 'node24');
        assert.equal(selectedRunner(service, 1, 0), 'node20_1');
        const repeated = runRefresh(service);
        assert.equal(repeated.service, service);
        assert.equal(repeated.backup, null);
    });
}

test('upgrade replaces an outdated managed region and keeps customizations', () => {
    const region = managedRegion(stockService);
    const outdated = stockService
        .replace(region, region.replace(/node24/g, 'node22'))
        .replace('# insert anything to setup env when running as a service\n',
            '# insert anything to setup env when running as a service\nexport CUSTOM_ENV=keep-me\n');
    const { result, service, backup } = runRefresh(outdated);
    assert.match(result.stdout, /EXIT:0/, result.stderr);
    assert.equal(backup, outdated);
    assert.equal(service, outdated.replace(region.replace(/node24/g, 'node22'), region));
});

test('upgrade leaves an up-to-date service script untouched', () => {
    const { result, service, files } = runRefresh(stockService);
    assert.match(result.stdout, /EXIT:0/, result.stderr);
    assert.equal(service, stockService);
    assert.deepEqual(files, ['bin', 'runsvc.sh', 'update.log']);
});

for (const [name, content] of Object.entries({
    'unrecognized launch command': '#!/bin/bash\nexec /opt/node/bin/node ./bin/AgentService.js\n',
    'unterminated managed region': stockService.replace(END, '# end'),
    'malformed fallback block': history[0].content.replace('\nfi\n\n# run', '\n\n# run'),
    'custom code after fallback block': history[0].content.replace(
        '# run the host process', 'export CUSTOM_AFTER_SELECTION=keep-me\n# run the host process')
})) {
    test(`upgrade skips service script with ${name}`, () => {
        const { result, service, files, log } = runRefresh(content);
        assert.match(result.stdout, /EXIT:0/, result.stderr);
        assert.equal(service, content);
        assert.deepEqual(files, ['bin', 'runsvc.sh', 'update.log']);
        assert.match(log, /svc\.sh uninstall/);
    });
}

test('upgrade does nothing when the agent is not installed as a service', () => {
    const { result, files } = runRefresh(undefined);
    assert.match(result.stdout, /EXIT:0/, result.stderr);
    assert.ok(!files.includes('runsvc.sh'));
});
