const assert = require('node:assert/strict');
const { test } = require('node:test');
const { ensureAlpineBuilds } = require('./alpine-node-build');

const checked = ['22.23.4', '24.23.0', '24.23.0'].map(latestVersion => ({ outdated: true, latestVersion }));
const build = (id, nodeVersion, status = 'completed', result = 'succeeded') =>
    ({ id, templateParameters: { nodeVersion }, status, result });
const response = (data, token) => new Response(JSON.stringify(data),
    { headers: token ? { 'x-ms-continuationtoken': token } : {} });
const run = (pins, fetchImpl) => ensureAlpineBuilds(pins, { token: 'test-token', fetchImpl });

test('duplicate detection: exact versions across all statuses/pages, newest match, and early exit', async () => {
    for (const [status, result] of [['notStarted', null], ['inProgress', null], ['cancelling', null],
        ['completed', 'succeeded'], ['completed', 'failed'], ['completed', 'canceled'], ['completed', 'partiallySucceeded']]) {
        let calls = 0;
        const results = await run([checked[1]], async (url, options) => {
            assert.equal(options.method, 'GET', 'Existing versions must never queue a run');
            if (++calls === 1) {
                return response({ value: [build(10, '24.22.1'), build(9, ''), { id: 8 }] }, 'next page+/=');
            }
            assert.equal(new URL(url).searchParams.get('continuationToken'), 'next page+/=');
            assert.equal(calls, 2, 'Stop as soon as the target is found');
            return response({ value: [build(7, 'v24.23.0', status, result), build(6, '24.23.0')] }, 'not-needed');
        });
        assert.deepEqual(results.map(item => [item.outcome, item.runId, item.status, item.result]),
            [['existing', 7, status, result]]);
    }
    let calls = 0;
    const results = await run(checked, async (url, options) => {
        assert.equal(options.method, 'GET');
        assert.equal(++calls, 1);
        return response({ value: [build(30, '24.23.0'), build(29, '22.23.4'), build(28, '24.23.0')] }, 'not-needed');
    });
    assert.deepEqual(results.map(item => item.runId), [29, 30]);
});

test('queue contract: unique latest stable targets, correct inputs, scoped authentication, and no-op', async () => {
    const posted = [];
    const results = await run(checked, async (url, options) => {
        const parsed = new URL(url);
        assert.equal(parsed.origin, 'https://dev.azure.com');
        assert.equal(parsed.searchParams.get('api-version'), '7.1');
        assert.equal(options.headers.Authorization, 'Bearer test-token');
        assert.equal(options.redirect, 'error');
        assert.ok(options.signal instanceof AbortSignal);
        if (options.method === 'GET') {
            assert.equal(parsed.searchParams.get('definitions'), '21894');
            assert.equal(parsed.searchParams.get('$top'), '100');
            assert.equal(parsed.searchParams.get('queryOrder'), 'queueTimeDescending');
            assert.equal(parsed.searchParams.has('statusFilter'), false);
            assert.equal(parsed.searchParams.has('branchName'), false);
            return response({ value: [build(1, '24.22.1')] });
        }
        assert.equal(parsed.pathname, '/mseng/AzureDevOps/_apis/pipelines/21894/runs');
        assert.equal(options.headers['Content-Type'], 'application/json');
        const body = JSON.parse(options.body);
        assert.equal(body.resources.repositories.self.refName, 'refs/heads/master');
        assert.equal(body.templateParameters.poolName, '1ES-ABTT-Shared-ARM-64-Pool');
        posted.push(body.templateParameters.nodeVersion);
        return response({ id: posted.length, state: 'inProgress' });
    });
    assert.deepEqual(posted, ['22.23.4', '24.23.0']);
    assert.ok(results.every(item => item.outcome === 'queued' && item.url.includes(`buildId=${item.runId}`)));
    assert.deepEqual(await ensureAlpineBuilds([{ outdated: false }], {
        fetchImpl: () => assert.fail('No token or request is needed for a no-op')
    }), []);
});

test('failure safety: incomplete history never queues, and failed/ambiguous POSTs never retry', async () => {
    await assert.rejects(ensureAlpineBuilds(checked, {}), /System.AccessToken/);
    await assert.rejects(run([{ outdated: true, latestVersion: 'bad' }], () => assert.fail()), /version/i);
    for (const invalid of [null, {}, { value: null }, { value: [null] }, { value: [{ id: 'bad' }] }]) {
        await assert.rejects(run(checked, async (url, options) => {
            assert.equal(options.method, 'GET');
            return response(invalid);
        }), /history|build/i);
    }
    for (const failure of [() => new Response('private body', { status: 403 }),
        () => new Response('invalid JSON'), () => { throw new Error('network unavailable'); }]) {
        let calls = 0;
        await assert.rejects(run(checked, async (url, options) => {
            assert.equal(options.method, 'GET');
            return ++calls === 1 ? response({ value: [build(1, '24.23.0')] }, 'next') : failure();
        }), error => !/test-token|private body/.test(error.message));
        assert.equal(calls, 2);
    }
    let pages = 0;
    await assert.rejects(run(checked, async (url, options) => {
        assert.equal(options.method, 'GET');
        assert.ok(++pages <= 2);
        return response({ value: [] }, 'repeated');
    }), /continuation/i);
    for (const failure of [() => new Response('', { status: 403 }),
        () => response({ state: 'inProgress' }), () => { throw new Error('network unavailable'); }]) {
        let posts = 0;
        const results = await run(checked, async (url, options) => {
            if (options.method === 'GET') {
                return response({ value: [] });
            }
            return ++posts === 1 ? failure() : response({ id: 20, state: 'inProgress' });
        });
        assert.equal(posts, 2);
        assert.deepEqual(results.map(item => item.outcome), ['error', 'queued']);
        assert.ok(results[0].error);
    }
});
