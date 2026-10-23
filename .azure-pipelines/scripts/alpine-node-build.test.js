const assert = require('node:assert/strict');
const { test } = require('node:test');
const { ensureAlpineBuilds } = require('./alpine-node-build');

const projectUrl = 'https://dev.azure.com/mseng/AzureDevOps';
const checked = [
    { variable: 'NODE22_VERSION', outdated: true, latestVersion: '22.23.4', securityUpdateRequired: true },
    { variable: 'NODE24_VERSION', outdated: true, latestVersion: '24.23.0', latestSecurityVersion: '24.22.1', securityUpdateRequired: true },
    { variable: 'NODE24_WIN_ARM64_VERSION', outdated: true, latestVersion: '24.23.0' },
    { variable: 'NODE26_VERSION', outdated: false, latestVersion: '26.12.1' }
];

function response(data, continuation) {
    return new Response(JSON.stringify(data), {
        headers: continuation ? { 'x-ms-continuationtoken': continuation } : {}
    });
}

function build(id, nodeVersion, status = 'completed', result = 'succeeded') {
    return { id, templateParameters: { nodeVersion }, status, result };
}

test('queues each missing latest stable version once with the verified Alpine inputs', async () => {
    const requests = [];
    const results = await ensureAlpineBuilds(checked, {
        token: 'test-token',
        fetchImpl: async (url, options) => {
            requests.push({ url: String(url), options });
            assert.equal(options.headers.Authorization, 'Bearer test-token');
            assert.equal(options.redirect, 'error');
            assert.ok(options.signal instanceof AbortSignal);
            if (options.method === 'GET') {
                const query = new URL(url).searchParams;
                assert.equal(query.get('definitions'), '21894');
                assert.equal(query.get('$top'), '100');
                assert.equal(query.get('queryOrder'), 'queueTimeDescending');
                assert.equal(query.get('api-version'), '7.1');
                assert.equal(query.has('statusFilter'), false);
                assert.equal(query.has('branchName'), false);
                return response({ value: [] });
            }
            assert.equal(String(url), `${projectUrl}/_apis/pipelines/21894/runs?api-version=7.1`);
            assert.equal(options.headers['Content-Type'], 'application/json');
            const body = JSON.parse(options.body);
            assert.equal(body.resources.repositories.self.refName, 'refs/heads/master');
            assert.equal(body.templateParameters.poolName, '1ES-ABTT-Shared-ARM-64-Pool');
            return response({ id: 100 + requests.length, state: 'inProgress' });
        }
    });
    assert.deepEqual(requests.filter(request => request.options.method === 'POST')
        .map(request => JSON.parse(request.options.body).templateParameters.nodeVersion), ['22.23.4', '24.23.0']);
    assert.deepEqual(results.map(result => [result.nodeVersion, result.outcome]), [
        ['22.23.4', 'queued'], ['24.23.0', 'queued']
    ]);
    assert.ok(results.every(result => result.url.includes(`buildId=${result.runId}`)));
});

test('checks all history pages and suppresses existing versions regardless of run status or result', async () => {
    const statuses = [
        ['notStarted', null], ['inProgress', null], ['cancelling', null],
        ['completed', 'succeeded'], ['completed', 'failed'], ['completed', 'canceled'],
        ['completed', 'partiallySucceeded']
    ];
    for (const [status, result] of statuses) {
        let calls = 0;
        const results = await ensureAlpineBuilds([checked[1]], {
            token: 'test-token',
            fetchImpl: async (url, options) => {
                assert.equal(options.method, 'GET', 'Existing runs must never cause a POST');
                calls++;
                if (calls === 1) {
                    return response({ value: [build(10, '24.22.1')] }, 'next page+/=');
                }
                assert.equal(new URL(url).searchParams.get('continuationToken'), 'next page+/=');
                return response({ value: [build(9, 'v24.23.0', status, result)] });
            }
        });
        assert.equal(calls, 2);
        assert.deepEqual(results.map(item => [item.nodeVersion, item.outcome, item.runId, item.status, item.result]),
            [['24.23.0', 'existing', 9, status, result]]);
    }
});

test('does not confuse another Node version or a missing default with an exact version match', async () => {
    let queued = 0;
    const results = await ensureAlpineBuilds([checked[1]], {
        token: 'test-token',
        fetchImpl: async (url, options) => {
            if (options.method === 'GET') {
                return response({ value: [build(3, '24.22.1'), build(2, ''), { id: 1 }] });
            }
            queued++;
            return response({ id: 4, state: 'inProgress' });
        }
    });
    assert.equal(queued, 1);
    assert.equal(results[0].nodeVersion, '24.23.0');
});

test('does not call Azure DevOps or require a token when no eligible pins are outdated', async () => {
    assert.deepEqual(await ensureAlpineBuilds([checked[3]], {
        fetchImpl: () => assert.fail('No builds should be queried')
    }), []);
});

test('rejects missing credentials and invalid history before queuing anything', async () => {
    await assert.rejects(ensureAlpineBuilds(checked, {}), /System.AccessToken/);
    for (const invalid of [null, {}, { value: null }, { value: [null] }, { value: [{ id: 'bad' }] }]) {
        await assert.rejects(ensureAlpineBuilds(checked, {
            token: 'test-token',
            fetchImpl: async (url, options) => {
                assert.equal(options.method, 'GET');
                return response(invalid);
            }
        }), /history|build/i);
    }
    let calls = 0;
    await assert.rejects(ensureAlpineBuilds(checked, {
        token: 'test-token',
        fetchImpl: async (url, options) => {
            assert.equal(options.method, 'GET');
            assert.ok(++calls <= 2, 'Repeated continuation tokens must not loop');
            return response({ value: [] }, 'repeated');
        }
    }), /continuation/i);
});

test('fails closed on history HTTP, JSON, and network errors without leaking credentials', async () => {
    for (const fetchImpl of [
        async () => new Response('private response text', { status: 403 }),
        async () => new Response('invalid JSON'),
        async () => { throw new Error('network unavailable'); }
    ]) {
        await assert.rejects(ensureAlpineBuilds(checked, { token: 'test-token', fetchImpl }), error => {
            assert.doesNotMatch(error.message, /test-token|private response text/);
            return true;
        });
    }
});

test('reports each queue failure, continues other versions, and never retries an ambiguous POST', async () => {
    let posts = 0;
    const results = await ensureAlpineBuilds(checked, {
        token: 'test-token',
        fetchImpl: async (url, options) => {
            if (options.method === 'GET') {
                return response({ value: [] });
            }
            posts++;
            return posts === 1 ? new Response('', { status: 403 }) : response({ id: 20, state: 'inProgress' });
        }
    });
    assert.equal(posts, 2);
    assert.equal(results[0].outcome, 'error');
    assert.match(results[0].error, /403|Queue builds/i);
    assert.equal(results[1].outcome, 'queued');

    const ambiguous = await ensureAlpineBuilds([checked[1]], {
        token: 'test-token',
        fetchImpl: async (url, options) => options.method === 'GET'
            ? response({ value: [] }) : response({ state: 'inProgress' })
    });
    assert.equal(ambiguous[0].outcome, 'error');
    assert.match(ambiguous[0].error, /run|response/i);
});
