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

const header = `#!/bin/bash

# convert SIGTERM signal to SIGINT
trap 'kill -INT $PID' TERM INT

if [ -f ".path" ]; then
    # configure
    export PATH=\`cat .path\`
    echo ".path=\${PATH}"
fi

# insert anything to setup env when running as a service
export CUSTOM_ENV=keep-me
`;
const tail = 'PID=$!\nwait $PID\ntrap - TERM INT\nwait $PID\n';
const launchComment = '# run the host process which keep the listener alive\n';

const legacyServices = {
    'node16 only': header + '\n' + launchComment +
        './externals/node16/bin/node ./bin/AgentService.js &\n' + tail,
    'node20 with node16 fallback': header + `
# fallback on Node16 if Node20 is not supported by the host
./externals/node20_1/bin/node --version
if [ $? == 0 ]; then
    NODE_VER="node20_1"
else    
    NODE_VER="node16"
fi

` + launchComment + './externals/"$NODE_VER"/bin/node ./bin/AgentService.js &\n' + tail,
    'node24 with node16 fallback': header + `
# Prefer Node24, fall back to Node20, then Node16 only if present
./externals/node24/bin/node --version 2>/dev/null
if [ $? == 0 ]; then
    NODE_VER="node24"
else
    ./externals/node20_1/bin/node --version 2>/dev/null
    if [ $? == 0 ]; then
        NODE_VER="node20_1"
    elif [ -f "./externals/node16/bin/node" ]; then
        NODE_VER="node16"
    else
        exit 1
    fi
fi

` + launchComment + './externals/"$NODE_VER"/bin/node ./bin/AgentService.js &\n' + tail
};

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
        .replace('./externals/"$NODE_VER"/bin/node ./bin/AgentService.js &',
            'printf "SERVICE_RUNNER:%s\\n" "$NODE_VER" &');
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

for (const [name, legacy] of Object.entries(legacyServices)) {
    test(`upgrade refreshes legacy service script (${name}) and keeps customizations`, () => {
        const { result, service, backup } = runRefresh(legacy);
        assert.match(result.stdout, /EXIT:0/, result.stderr);
        assert.equal(backup, legacy);
        assert.equal(managedRegion(service), managedRegion(stockService));
        assert.match(service, /^export CUSTOM_ENV=keep-me$/m);
        assert.doesNotMatch(service, /node16/);
        assert.equal(service.match(LAUNCH).length, 1);
        assert.equal(service.split(launchComment).length, 2);
        assert.ok(service.endsWith(tail));
        assert.equal(selectedRunner(service, 0, 0), 'node24');
        assert.equal(selectedRunner(service, 1, 0), 'node20_1');
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
    'unterminated managed region': stockService.replace(END, '# end')
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
