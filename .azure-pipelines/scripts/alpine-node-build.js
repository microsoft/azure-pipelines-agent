const semver = require('semver');

const projectUrl = 'https://dev.azure.com/mseng/AzureDevOps';
const pipelineId = 21894;

async function request(url, token, fetchImpl, method = 'GET', body) {
    const response = await fetchImpl(url, {
        method,
        headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
        redirect: 'error',
        signal: AbortSignal.timeout(60000),
        ...(body ? { body: JSON.stringify(body) } : {})
    });
    if (!response.ok) {
        throw new Error(`Alpine-NodeJs-Build ${method} request failed: HTTP ${response.status}. ` +
            'The monitoring build identity needs View builds and Queue builds permissions on pipeline 21894.');
    }
    return { data: await response.json(), continuation: response.headers.get('x-ms-continuationtoken') };
}

async function readBuildHistory(versions, token, fetchImpl) {
    const matches = new Map();
    const wanted = new Set(versions);
    const seenTokens = new Set();
    let continuation;
    do {
        const url = new URL(`${projectUrl}/_apis/build/builds`);
        url.search = new URLSearchParams({
            definitions: String(pipelineId), '$top': '100',
            queryOrder: 'queueTimeDescending', 'api-version': '7.1',
            ...(continuation ? { continuationToken: continuation } : {})
        });
        const page = await request(url, token, fetchImpl);
        if (!page.data || !Array.isArray(page.data.value) || page.data.value.some(build =>
            !build || !Number.isSafeInteger(build.id) || build.id <= 0 ||
            (build.templateParameters !== undefined && (!build.templateParameters ||
                typeof build.templateParameters !== 'object' || Array.isArray(build.templateParameters))))) {
            throw new Error('Invalid Alpine-NodeJs-Build history; no builds will be queued.');
        }
        for (const build of page.data.value) {
            const value = build.templateParameters?.nodeVersion;
            const version = typeof value === 'string' ? semver.clean(value.trim()) : null;
            if (wanted.has(version) && !matches.has(version)) {
                matches.set(version, build);
            }
        }
        if (matches.size === wanted.size) {
            break;
        }
        continuation = page.continuation;
        if (continuation && seenTokens.has(continuation)) {
            throw new Error('Repeated continuation token in Alpine-NodeJs-Build history; no builds will be queued.');
        }
        seenTokens.add(continuation);
    } while (continuation);
    return matches;
}

function runDetails(run, queued = false) {
    if (!run || !Number.isSafeInteger(run.id) || run.id <= 0) {
        throw new Error('Invalid Alpine-NodeJs-Build run response. Check history before retrying the request.');
    }
    return {
        runId: run.id,
        status: (queued ? run.state : run.status) || 'unknown',
        result: run.result || null,
        url: `${projectUrl}/_build/results?buildId=${run.id}`
    };
}

async function ensureAlpineBuilds(checked, { token, fetchImpl = fetch }) {
    const versions = [...new Set(checked.filter(pin => pin.outdated).map(pin => pin.latestVersion))];
    if (versions.length === 0) {
        return [];
    }
    if (typeof token !== 'string' || !token.trim()) {
        throw new Error('System.AccessToken is required to check and queue Alpine-NodeJs-Build runs.');
    }
    if (versions.some(version => !semver.valid(version) || semver.prerelease(version))) {
        throw new Error('Invalid target Node version for Alpine-NodeJs-Build.');
    }
    const history = await readBuildHistory(versions, token, fetchImpl);
    const results = [];
    for (const nodeVersion of versions) {
        const existing = history.get(nodeVersion);
        if (existing) {
            results.push({ nodeVersion, outcome: 'existing', ...runDetails(existing) });
            continue;
        }
        try {
            // Do not retry a POST: a timeout can occur after Azure DevOps accepts the run.
            const { data } = await request(
                `${projectUrl}/_apis/pipelines/${pipelineId}/runs?api-version=7.1`, token, fetchImpl, 'POST', {
                    resources: { repositories: { self: { refName: 'refs/heads/master' } } },
                    templateParameters: { nodeVersion, poolName: '1ES-ABTT-Shared-ARM-64-Pool' }
                });
            results.push({ nodeVersion, outcome: 'queued', ...runDetails(data, true) });
        } catch (error) {
            results.push({ nodeVersion, outcome: 'error', error: error.message });
        }
    }
    return results;
}

module.exports = { ensureAlpineBuilds };
