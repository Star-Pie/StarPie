import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { configuration, syncRelease } from './sync-atomgit-release.mjs';

const COMMIT = 'a'.repeat(40);
const OTHER = 'b'.repeat(40);
const digest = (value) => createHash('sha256').update(value).digest('hex');
const json = (value, status = 200) => new Response(JSON.stringify(value), {
  status, headers: { 'content-type': 'application/json' },
});

function fixture(options = {}) {
  const config = { ...configuration({
    RELEASE_TAG: 'v2.0.0', GITHUB_REPOSITORY: 'Star-Pie/StarPie',
    GITHUB_TOKEN: 'github-test-secret', ATOMGIT_TOKEN: 'atomgit-test-secret',
  }), tagAttempts: 3, tagIntervalMs: 1, visibilityAttempts: 3, visibilityIntervalMs: 1 };
  const files = options.files || [{ id: 11, name: 'StarPie.zip', content: Buffer.from('signed original bytes') }];
  const source = {
    id: 10, tag_name: config.tag, name: 'StarPie v2.0.0', body: 'Release notes',
    draft: false, published_at: '2026-10-23T00:00:00Z', prerelease: false, ...options.source,
  };
  let target = options.existing ? {
    tag_name: config.tag, target_commitish: COMMIT, name: source.name, body: source.body,
    release_status: source.prerelease ? 'pre' : 'latest', assets: [], ...options.target,
  } : null;
  const stored = new Map(options.stored || []);
  let tagReads = 0;
  let releaseReads = 0;
  let pending;
  const calls = [];
  const logs = [];
  const sleeps = [];
  function targetView() {
    return { ...target, assets: [...stored.keys()].map((name) => ({
      name, browser_download_url: `https://downloads.atomgit.example/${encodeURIComponent(name)}`,
    })) };
  }
  const fetchImpl = async (input, init = {}) => {
    const url = new URL(input);
    const method = init.method || 'GET';
    const call = { host: url.host, path: url.pathname, method, headers: init.headers, query: url.searchParams };
    calls.push(call);
    assert.equal(init.redirect, 'manual');
    if (options.intercept) {
      const override = await options.intercept({ url, init, call, calls });
      if (override) return override;
    }
    if (url.host === 'api.github.com') {
      assert.equal(init.headers.Authorization, `Bearer ${config.githubToken}`);
      if (url.pathname.endsWith('/releases/tags/v2.0.0')) return json(source);
      if (url.pathname.endsWith('/releases/10/assets')) {
        const page = Number(url.searchParams.get('page'));
        return json(files.slice((page - 1) * 100, page * 100).map((file) => ({
          id: file.id, name: file.name, size: file.content.length, state: 'uploaded',
          digest: `sha256:${digest(file.content)}`, ...options.assetOverride,
        })));
      }
      if (url.pathname.endsWith('/git/ref/tags/v2.0.0')) {
        return json({ object: { type: options.annotated ? 'tag' : 'commit', sha: COMMIT } });
      }
      if (url.pathname.includes('/git/tags/')) return json({ object: { type: 'commit', sha: COMMIT } });
      if (url.pathname.endsWith('/releases/latest')) return json({ id: options.olderStable ? 99 : source.id });
      const match = url.pathname.match(/\/releases\/assets\/(\d+)$/);
      if (match) return new Response(files.find((file) => file.id === Number(match[1])).content);
    }
    if (url.host === 'api.atomgit.com') {
      assert.equal(url.searchParams.get('access_token'), config.atomgitToken);
      assert.equal(init.headers.Authorization, undefined);
      if (url.pathname.endsWith('/tags')) {
        tagReads++;
        if (options.missingTag || tagReads <= (options.tagDelay || 0)) return json([]);
        return json([{ name: config.tag, commit: { sha: options.tagCommit || COMMIT } }]);
      }
      if (url.pathname.endsWith('/releases/tags/v2.0.0')) {
        releaseReads++;
        if (pending && releaseReads >= pending.visibleAt) {
          stored.set(pending.name, pending.content);
          pending = null;
        }
        return target ? json(targetView()) : json({ message: 'not found' }, 404);
      }
      if (method === 'POST' && url.pathname.endsWith('/releases')) {
        assert.equal(target, null);
        const body = JSON.parse(init.body);
        assert.equal(body.target_commitish, COMMIT);
        assert.equal(body.tag_name, config.tag);
        target = { ...body, prerelease: body.release_status === 'pre', assets: [] };
        return json(targetView(), 200);
      }
      if (url.pathname.endsWith('/upload_url')) {
        return json({
          url: `https://storage.atomgit.example/${encodeURIComponent(url.searchParams.get('file_name'))}`,
          headers: { 'Content-Type': 'application/octet-stream', 'x-obs-callback': 'storage-callback' },
        });
      }
      if (method === 'PATCH') {
        Object.assign(target, JSON.parse(init.body));
        target.prerelease = target.release_status === 'pre';
        return json(targetView());
      }
    }
    if (url.host === 'storage.atomgit.example') {
      assert.equal(method, 'PUT');
      assert.equal(init.headers.Authorization, undefined);
      assert.equal(JSON.stringify(init.headers).includes(config.atomgitToken), false);
      const chunks = [];
      for await (const chunk of init.body) chunks.push(chunk);
      const content = Buffer.concat(chunks);
      assert.equal(Number(init.headers['Content-Length']), content.length);
      const name = decodeURIComponent(url.pathname.slice(1));
      pending = { name, content, visibleAt: releaseReads + (options.visibilityDelay || 1) };
      if (options.uploadLostResponse) throw new Error(`network ${config.atomgitToken}`);
      return new Response(null, { status: 200 });
    }
    if (url.host === 'downloads.atomgit.example') {
      assert.equal(init.headers.Authorization, undefined);
      return new Response(stored.get(decodeURIComponent(url.pathname.slice(1))));
    }
    throw new Error('Unexpected mock request');
  };
  return {
    config, calls, logs, sleeps, stored,
    run: () => syncRelease(config, { fetchImpl, sleep: async (ms) => sleeps.push(ms), log: (line) => logs.push(line) }),
  };
}
const writes = (f) => f.calls.filter((call) => ['POST', 'PUT', 'PATCH', 'DELETE'].includes(call.method));

test('configuration requires secrets and rejects unsafe repository/tag input', () => {
  const env = { RELEASE_TAG: 'v2.0.0', GITHUB_REPOSITORY: 'Star-Pie/StarPie', GITHUB_TOKEN: 'gh', ATOMGIT_TOKEN: 'ag' };
  assert.throws(() => configuration({ ...env, ATOMGIT_TOKEN: '' }), /ATOMGIT_TOKEN is required/);
  for (const tag of ['../main', 'v2\nattack', 'x.lock', 'x//y', '${{ secrets.TOKEN }}']) {
    assert.throws(() => configuration({ ...env, RELEASE_TAG: tag }), /supported Git tag/);
  }
  assert.throws(() => configuration({ ...env, ATOMGIT_REPOSITORY: '../repo' }), /owner\/repository/);
  assert.equal(configuration({ ...env, ATOMGIT_REPOSITORY: 'another-org/mirror' }).target, 'another-org/mirror');
});

test('new release copies exact bytes, verifies hash and uses documented query authentication', async () => {
  const f = fixture();
  assert.deepEqual(await f.run(), { uploaded: 1, skipped: 0, verified: 1, commit: COMMIT });
  assert.deepEqual(writes(f).map((call) => call.method), ['POST', 'PUT']);
  assert.equal(f.logs.join('\n').includes(f.config.atomgitToken), false);
});

test('annotated tags resolve to their commit before publication', async () => {
  const f = fixture({ annotated: true });
  await f.run();
  assert.equal(f.calls.some((call) => call.path.includes('/git/tags/')), true);
});

test('existing identical attachment is downloaded and verified, not overwritten', async () => {
  const f = fixture({ existing: true, stored: [['StarPie.zip', Buffer.from('signed original bytes')]] });
  assert.equal((await f.run()).skipped, 1);
  assert.equal(writes(f).length, 0);
});

test('partial release retry reuses completed files and only uploads missing files', async () => {
  const files = [{ id: 11, name: 'a.zip', content: Buffer.from('a') }, { id: 12, name: 'b.exe', content: Buffer.from('b') }];
  const f = fixture({ existing: true, files, stored: [['a.zip', Buffer.from('a')]] });
  assert.equal((await f.run()).uploaded, 1);
  assert.deepEqual(writes(f).map((call) => call.method), ['PUT']);
});

test('same-name different-content attachment fails without writes', async () => {
  const f = fixture({ existing: true, stored: [['StarPie.zip', Buffer.from('tampered')]] });
  await assert.rejects(f.run(), /conflicts with GitHub/);
  assert.equal(writes(f).length, 0);
});

for (const [name, source] of [['draft', { draft: true }], ['unpublished', { published_at: null }], ['wrong tag', { tag_name: 'v9' }]]) {
  test(`${name} source is rejected before any write`, async () => {
    const f = fixture({ source });
    await assert.rejects(f.run(), /already-published/);
    assert.equal(writes(f).length, 0);
  });
}

test('release without binary attachments is rejected', async () => {
  const f = fixture({ files: [] });
  await assert.rejects(f.run(), /no uploaded assets/);
  assert.equal(writes(f).length, 0);
});

test('unmirrored tag waits a bounded time then fails without creating a tag', async () => {
  const f = fixture({ missingTag: true });
  await assert.rejects(f.run(), /not mirrored yet/);
  assert.equal(f.sleeps.length, 2);
  assert.equal(writes(f).length, 0);
});

test('mirror delay is tolerated and mismatched commit is never overwritten', async () => {
  const delayed = fixture({ tagDelay: 1 });
  await delayed.run();
  assert.equal(delayed.sleeps.length, 1);
  const wrong = fixture({ tagCommit: OTHER });
  await assert.rejects(wrong.run(), /different commit/);
  assert.equal(writes(wrong).length, 0);
});

test('existing release bound to another commit is rejected', async () => {
  const f = fixture({ existing: true, target: { target_commitish: OTHER } });
  await assert.rejects(f.run(), /tag\/commit differs/);
  assert.equal(writes(f).length, 0);
});

test('source digest/size checks catch damaged or incomplete downloads', async () => {
  for (const assetOverride of [{ digest: `sha256:${'0'.repeat(64)}` }, { size: 999 }, { state: 'new' }]) {
    const f = fixture({ assetOverride });
    await assert.rejects(f.run(), /integrity check failed|incomplete or invalid asset/);
    assert.equal(writes(f).length, 0);
  }
});

test('duplicate or path-traversal asset names fail safely', async () => {
  for (const names of [['same', 'same'], ['../escape'], ['a\\b']]) {
    const f = fixture({ files: names.map((name, id) => ({ id: id + 11, name, content: Buffer.from('x') })) });
    await assert.rejects(f.run(), /unsafe asset name|duplicate asset/);
    assert.equal(writes(f).length, 0);
  }
});

test('delayed upload callback is polled and then verified', async () => {
  const f = fixture({ visibilityDelay: 2 });
  await f.run();
  assert.equal(f.sleeps.length, 1);
});

test('lost PUT response is not blindly retried; subsequent run verifies completed upload', async () => {
  const f = fixture({ uploadLostResponse: true });
  await assert.rejects(f.run(), (error) => /Asset upload failed/.test(error.message) && !error.message.includes(f.config.atomgitToken));
  assert.equal(writes(f).filter((call) => call.method === 'PUT').length, 1);
  assert.equal((await f.run()).skipped, 1);
});

test('pre-release status and changed notes are synchronized only after attachment verification', async () => {
  const f = fixture({ existing: true, source: { prerelease: true },
    target: { body: 'old notes', release_status: 'latest' }, stored: [['StarPie.zip', Buffer.from('signed original bytes')]] });
  await f.run();
  const patch = f.calls.findIndex((call) => call.method === 'PATCH');
  const verification = f.calls.findIndex((call) => call.host === 'downloads.atomgit.example');
  assert.ok(patch > verification);
});

test('older stable version does not explicitly request latest promotion', async () => {
  const f = fixture({ olderStable: true, intercept: ({ init }) => {
    if (init.method === 'POST') assert.equal(JSON.parse(init.body).release_status, undefined);
  } });
  await f.run();
});

test('API authentication error is reported without response body or token', async () => {
  const f = fixture({ intercept: ({ url }) => url.host === 'api.atomgit.com' ? json({ error: 'secret atomgit-test-secret' }, 403) : null });
  await assert.rejects(f.run(), (error) => /HTTP 403/.test(error.message) && !error.message.includes('atomgit-test-secret'));
  assert.equal(writes(f).length, 0);
});

test('transient read failure retries, while write failure requires manual retry', async () => {
  let reads = 0;
  const transient = fixture({ intercept: ({ url }) => {
    if (url.pathname.endsWith('/tags') && ++reads === 1) return json({}, 503);
  } });
  await transient.run();
  assert.equal(transient.sleeps.length, 1);
  const failure = fixture({ intercept: ({ init }) => init.method === 'POST' ? json({}, 503) : null });
  await assert.rejects(failure.run(), /HTTP 503/);
  assert.equal(writes(failure).length, 1);
});

test('GitHub download redirect strips authorization before crossing origins', async () => {
  const f = fixture({ intercept: ({ url, init }) => {
    if (url.pathname.includes('/releases/assets/')) return new Response(null, { status: 302, headers: { location: 'https://github-download.example/package' } });
    if (url.host === 'github-download.example') {
      assert.equal(init.headers.Authorization, undefined);
      return new Response('signed original bytes');
    }
  } });
  await f.run();
});

test('upload redirects, unsafe URLs and credential-bearing upload headers are rejected', async () => {
  for (const upload of [
    { url: 'http://storage.example/file', headers: {} },
    { url: 'https://storage.example/file', headers: { Authorization: 'Bearer atomgit-test-secret' } },
  ]) {
    const f = fixture({ intercept: ({ url }) => url.pathname.endsWith('/upload_url') ? json(upload) : null });
    await assert.rejects(f.run(), /unsafe transfer URL|unsafe upload headers/);
    assert.equal(writes(f).filter((call) => call.method === 'PUT').length, 0);
  }
  const f = fixture({ intercept: ({ url }) => url.host === 'storage.atomgit.example' ? new Response(null, { status: 307, headers: { location: 'https://other.example' } }) : null });
  await assert.rejects(f.run(), /Asset upload failed/);
  assert.equal(f.calls.some((call) => call.host === 'other.example'), false);
});

test('uploaded bytes are downloaded back and hash mismatch is detected', async () => {
  const f = fixture({ intercept: ({ url }) => url.host === 'downloads.atomgit.example' ? new Response('wrong bytes') : null });
  await assert.rejects(f.run(), /conflicts with GitHub/);
  assert.equal(writes(f).some((call) => call.method === 'DELETE' || call.method === 'PATCH'), false);
});

test('source asset pagination includes all files, not only embedded assets', async () => {
  const files = Array.from({ length: 101 }, (_, i) => ({ id: i + 11, name: `asset-${i}`, content: Buffer.from(String(i)) }));
  const f = fixture({ existing: true, files, stored: files.map((file) => [file.name, file.content]) });
  assert.equal((await f.run()).verified, 101);
  assert.equal(f.calls.filter((call) => call.path.endsWith('/releases/10/assets')).length, 2);
});

test('workflow wiring is structural only: manual/reusable entry, minimal secrets and publication gate', async () => {
  const workflow = await readFile(new URL('../workflows/atomgit-release.yml', import.meta.url), 'utf8');
  const caller = await readFile(new URL('../workflows/release.yml', import.meta.url), 'utf8');
  assert.match(workflow, /workflow_dispatch:/);
  assert.match(workflow, /workflow_call:/);
  assert.match(workflow, /contents: read/);
  assert.match(workflow, /persist-credentials: false/);
  assert.doesNotMatch(workflow, /contents: write|pull_request_target:|SIGNPATH_API_TOKEN/);
  assert.match(caller, /published: \$\{\{ steps\.publish\.outputs\.published \}\}/);
  assert.match(caller, /always\(\) && !cancelled\(\) && needs\.release\.outputs\.published == 'true'/);
  assert.match(caller, /uses: \.\/\.github\/workflows\/atomgit-release\.yml/);
  assert.doesNotMatch(caller, /secrets: inherit/);
  assert.ok(caller.indexOf('"published=true"') > caller.indexOf('throw "GitHub Release creation failed'));
});

test('a later conflicting file blocks uploading an earlier missing file', async () => {
  const files = [{ id: 11, name: 'missing.zip', content: Buffer.from('a') }, { id: 12, name: 'conflict.zip', content: Buffer.from('b') }];
  const f = fixture({ existing: true, files, stored: [['conflict.zip', Buffer.from('bad')]] });
  await assert.rejects(f.run(), /conflicts with GitHub/);
  assert.equal(writes(f).length, 0);
});

test('final prerelease flag inconsistency is not reported as success', async () => {
  let reads = 0;
  const f = fixture({ existing: true, stored: [['StarPie.zip', Buffer.from('signed original bytes')]],
    intercept: ({ url }) => {
      if (url.host === 'api.atomgit.com' && url.pathname.endsWith('/releases/tags/v2.0.0') && ++reads > 1) {
        return json({ tag_name: 'v2.0.0', target_commitish: COMMIT, name: 'StarPie v2.0.0', body: 'Release notes',
          prerelease: true, release_status: 'latest', assets: [{ name: 'StarPie.zip', browser_download_url: 'https://downloads.atomgit.example/StarPie.zip' }] });
      }
    } });
  await assert.rejects(f.run(), /final release verification failed/);
});
