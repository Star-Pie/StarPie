import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { readFileSync, existsSync, statSync } from 'node:fs';
import { EventEmitter } from 'node:events';
import { PassThrough, Writable, Readable } from 'node:stream';
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
    UPLOAD_CLIENT: 'node', UPLOAD_TIMEOUT_MINUTES: '10', JOB_TIMEOUT_MINUTES: '60', ...options.env,
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
      for await (const chunk of init.body) {
        chunks.push(chunk);
        options.onUploadChunk?.(chunk);
      }
      options.afterUpload?.();
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
    config, calls, logs, sleeps, stored, fetchImpl,
    run: (dependencies = {}) => syncRelease(config, { fetchImpl, sleep: async (ms) => sleeps.push(ms), log: (line) => logs.push(line), ...(options.clock ? { clock: options.clock } : {}), ...dependencies }),
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

test('each asset logs transfer start, completion, visibility and readback before verification', async () => {
  const f = fixture();
  await f.run();
  const lines = f.logs.join('\n');
  for (const stage of ['GitHub download', 'AtomGit upload', 'AtomGit readback']) {
    assert.ok(f.logs.some(line => line.startsWith(stage + ' started: "StarPie.zip"')), stage + ' start');
    assert.ok(f.logs.some(line => line.startsWith(stage + ' completed: "StarPie.zip"')), stage + ' completion');
  }
  assert.match(lines, /Requesting AtomGit upload URL: "StarPie.zip"/);
  assert.match(lines, /Waiting for AtomGit attachment visibility: "StarPie.zip"/);
  assert.ok(lines.indexOf('AtomGit readback completed:') < lines.indexOf('Verified SHA-256:'));
  assert.match(lines, /21 B \/ 21 B/);
  assert.match(lines, /elapsed [0-9.]+s; average [0-9.]+ B\/s/);
  assert.doesNotMatch(lines, /github-test-secret|atomgit-test-secret|storage-callback|https:\/\//);
});

// Advance transfer heartbeats without sleeping or contacting a real service.
function progressClock() {
  let time = 0;
  const intervals = new Set();
  return {
    now: () => time,
    setInterval: (callback, interval) => {
      assert.equal(interval, 30_000);
      const timer = { callback, unref() {} };
      intervals.add(timer);
      return timer;
    },
    clearInterval: (timer) => { assert.equal(intervals.delete(timer), true); },
    advance: (ms = 30_000) => { time += ms; for (const timer of [...intervals]) timer.callback(); },
    active: () => intervals.size,
  };
}

test('download heartbeat reports stalled headers and partial bytes without waiting in real time', async () => {
  const clock = progressClock();
  const content = Buffer.alloc(2048, 'a');
  const f = fixture({ clock, files: [{ id: 11, name: 'Slow.zip', content }], intercept: ({ url }) => {
    if (!url.pathname.includes('/releases/assets/')) return;
    clock.advance();
    let part = 0;
    return new Response(new ReadableStream({
      async pull(controller) {
        if (part === 0) { part++; controller.enqueue(content.subarray(0, 1024)); }
        else if (part === 1) {
          part++;
          await new Promise(setImmediate);
          clock.advance();
          controller.enqueue(content.subarray(1024));
        } else { controller.close(); }
      },
    }));
  } });
  await f.run();
  const lines = f.logs.join('\n');
  assert.match(lines, /GitHub download progress: "Slow.zip"; 0 B \/ 2.0 KiB \(0.0%\); elapsed 30.0s; average 0 B\/s; waiting for response headers/);
  assert.match(lines, /GitHub download progress: "Slow.zip"; 1.0 KiB \/ 2.0 KiB \(50.0%\); elapsed 60.0s; average 17 B\/s; receiving body/);
  assert.equal(clock.active(), 0);
  const count = f.logs.length;
  clock.advance();
  assert.equal(f.logs.length, count);
});

test('upload heartbeat distinguishes streamed bytes from server confirmation', async () => {
  const clock = progressClock();
  const f = fixture({ clock,
    onUploadChunk: () => clock.advance(),
    afterUpload: () => {
      clock.advance();
      assert.equal(f.logs.some(line => line.startsWith('AtomGit upload completed:')), false);
      assert.ok(f.logs.some(line => line.includes('100.0%') && line.endsWith('body streamed; waiting for server confirmation.')));
    },
  });
  await f.run();
  assert.ok(f.logs.some(line => line.startsWith('AtomGit upload progress:') && line.includes('elapsed 60.0s')));
  assert.ok(f.logs.some(line => line.startsWith('AtomGit upload completed:')));
  assert.equal(clock.active(), 0);
});

test('readback heartbeat starts before waiting for AtomGit download headers', async () => {
  const clock = progressClock();
  const f = fixture({ clock, intercept: ({ url }) => {
    if (url.host === 'downloads.atomgit.example') clock.advance();
  } });
  await f.run();
  assert.ok(f.logs.some(line => line.startsWith('AtomGit readback progress:') && line.endsWith('waiting for response headers.')));
  assert.equal(clock.active(), 0);
});

for (const failure of ['download headers', 'download body', 'upload', 'readback']) {
  test('transfer failure cleans heartbeat and reports its stage: ' + failure, async () => {
    const clock = progressClock();
    const f = fixture({ clock, uploadLostResponse: failure === 'upload', intercept: ({ url }) => {
      if (url.pathname.includes('/releases/assets/') && failure.startsWith('download')) {
        clock.advance();
        if (failure === 'download body') return new Response(new ReadableStream({
          pull(controller) { controller.error(new Error('private-stream-error atomgit-test-secret')); },
        }));
        throw new Error('private-fetch-error atomgit-test-secret');
      }
      if (url.host === 'downloads.atomgit.example' && failure === 'readback') {
        clock.advance();
        throw new Error('private-readback-error github-test-secret');
      }
    } });
    await assert.rejects(f.run(), /Asset (download|upload)/);
    const stage = failure.startsWith('download') ? 'GitHub download' : failure === 'upload' ? 'AtomGit upload' : 'AtomGit readback';
    assert.ok(f.logs.some(line => line.startsWith(stage + ' failed: "StarPie.zip"')));
    assert.equal(clock.active(), 0);
    const count = f.logs.length;
    clock.advance();
    assert.equal(f.logs.length, count);
    assert.doesNotMatch(f.logs.join('\n'), /github-test-secret|atomgit-test-secret|private-.*error|https:\/\//);
  });
}

test('signed transfer URLs and upload headers never appear in progress logs', async () => {
  const f = fixture({ intercept: ({ url }) => {
    if (url.pathname.includes('/releases/assets/')) return new Response(null, {
      status: 302, headers: { location: 'https://github-download.example/package?signature=private-download-query' },
    });
    if (url.host === 'github-download.example') return new Response('signed original bytes');
    if (url.pathname.endsWith('/upload_url')) return json({
      url: 'https://storage.atomgit.example/StarPie.zip?signature=private-upload-query',
      headers: { 'x-obs-callback': 'private-callback-header' },
    });
  } });
  await f.run();
  assert.doesNotMatch(f.logs.join('\n'), /https:\/\/|private-|github-test-secret|atomgit-test-secret/);
});

test('credentials embedded in remote filenames are redacted from progress logs', async () => {
  const f = fixture({ files: [{ id: 11, name: 'github-test-secret-atomgit-test-secret.zip', content: Buffer.from('file') }] });
  await assert.rejects(f.run(), /unsafe transfer URL/);
  assert.match(f.logs.join('\n'), /GitHub download started: "\*\*\*-\*\*\*.zip"/);
  assert.doesNotMatch(f.logs.join('\n'), /github-test-secret|atomgit-test-secret/);
});

test('empty transfer logs have finite sizes and rates', async () => {
  const f = fixture({ files: [{ id: 11, name: 'empty.txt', content: Buffer.alloc(0) }] });
  await f.run();
  assert.match(f.logs.join('\n'), /0 B \/ 0 B \(n\/a\)/);
  assert.doesNotMatch(f.logs.join('\n'), /NaN|Infinity/);
});

test('upload progress preserves a body accepted by native Fetch Request normalization', async () => {
  const f = fixture({ intercept: ({ url, init }) => {
    if (url.host !== 'storage.atomgit.example') return;
    const request = new Request(url, init);
    assert.equal(request.headers.get('content-length'), '21');
    // Exercise native AsyncIterable body normalization without making a network request.
    init.body = request.body;
  } });
  assert.equal((await f.run()).uploaded, 1);
  assert.deepEqual(f.stored.get('StarPie.zip'), Buffer.from('signed original bytes'));
});

const configEnvironment = {
  RELEASE_TAG: 'v2.0.0', GITHUB_REPOSITORY: 'Star-Pie/StarPie',
  GITHUB_TOKEN: 'github-test-secret', ATOMGIT_TOKEN: 'atomgit-test-secret',
};
test('upload client defaults to curl with no active upload timeout and accepts explicit limits', () => {
  const defaults = configuration(configEnvironment);
  assert.equal(defaults.uploadClient, 'curl');
  assert.equal(defaults.uploadTimeoutMs, null);
  assert.equal(defaults.jobTimeoutMinutes, 360);
  const custom = configuration({ ...configEnvironment, UPLOAD_CLIENT: 'curl', UPLOAD_TIMEOUT_MINUTES: '30', JOB_TIMEOUT_MINUTES: '180' });
  assert.equal(custom.uploadClient, 'curl');
  assert.equal(custom.uploadTimeoutMs, 1_800_000);
  assert.equal(custom.jobTimeoutMinutes, 180);
});
test('unsafe upload clients and timeout values fail before synchronization', () => {
  for (const client of ['wget', 'curl --verbose', 'C:/custom/curl.exe'])
    assert.throws(() => configuration({ ...configEnvironment, UPLOAD_CLIENT: client }), /UPLOAD_CLIENT/);
  for (const value of ['0', '-1', '1.5', 'NaN', '1; echo secret']) {
    assert.throws(() => configuration({ ...configEnvironment, UPLOAD_TIMEOUT_MINUTES: value }), /UPLOAD_TIMEOUT_MINUTES/);
    assert.throws(() => configuration({ ...configEnvironment, JOB_TIMEOUT_MINUTES: value }), /JOB_TIMEOUT_MINUTES/);
  }
  assert.equal(configuration({ ...configEnvironment, UPLOAD_TIMEOUT_MINUTES: '720', JOB_TIMEOUT_MINUTES: '60' }).uploadTimeoutMs, 43_200_000);
});
test('manual and reusable workflows expose upload client and timeout controls', async () => {
  const workflow = await readFile(new URL('../workflows/atomgit-release.yml', import.meta.url), 'utf8');
  for (const name of ['upload_client', 'upload_timeout_minutes', 'job_timeout_minutes']) {
    assert.equal([...workflow.matchAll(new RegExp('^      ' + name + ':', 'gm'))].length, 2, name);
  }
  assert.match(workflow, /UPLOAD_CLIENT: \$\{\{ inputs.upload_client \}\}/);
  assert.match(workflow, /UPLOAD_TIMEOUT_MINUTES: \$\{\{ inputs.upload_timeout_minutes \}\}/);
  assert.match(workflow, /JOB_TIMEOUT_MINUTES: \$\{\{ inputs.job_timeout_minutes \}\}/);
  const timeout = workflow.match(/^    timeout-minutes: \$\{\{ (.+) \}\}/m)?.[1];
  assert.equal(timeout, "fromJSON(format('{0}', inputs.job_timeout_minutes || 360))");
});

// Model a curl process with in-memory pipes; all HTTP still goes through the fixture.
function curlProcess(f, behavior = {}) {
  const calls = [];
  function spawnImpl(command, args, options) {
    const path = args[2];
    const config = readFileSync(path, 'utf8');
    const call = { command, args, options, path, config, killed: false };
    calls.push(call);
    if (process.platform !== 'win32') assert.equal(statSync(path).mode & 0o077, 0);
    if (behavior.spawnThrow) throw new Error('private spawn error atomgit-test-secret');
    const child = new EventEmitter();
    child.stdout = new PassThrough();
    child.stderr = new PassThrough();
    let closed = false;
    function close(code) {
      if (closed) return;
      closed = true;
      child.stdout.end(); child.stderr.end();
      child.emit('close', code);
    }
    child.kill = () => { call.killed = true; child.stdin.destroy(); close(null); return true; };
    const chunks = [];
    child.stdin = new Writable({
      write(chunk, _encoding, callback) {
        if (behavior.stdinError) { callback(new Error('private stdin error')); return; }
        chunks.push(Buffer.from(chunk));
        behavior.onChunk?.();
        callback();
      },
      final(callback) {
        callback();
        setImmediate(async () => {
          if (closed) return;
          try {
            behavior.beforeResponse?.();
            const url = JSON.parse(config.match(/^url = (.+)$/m)[1]);
            const headers = Object.fromEntries([...config.matchAll(/^header = (.+)$/gm)].map(match => {
              const line = JSON.parse(match[1]);
              const colon = line.indexOf(':');
              return [line.slice(0, colon), line.slice(colon + 1).trim()];
            }));
            const response = await f.fetchImpl(new URL(url), {
              method: 'PUT', headers, body: Readable.from(chunks), redirect: 'manual',
            });
            child.stdout.write(behavior.output ?? String(response.status));
            child.stderr.write('private stderr https://storage.example/?signature=private-signature atomgit-test-secret');
            close(behavior.exitCode ?? 0);
          } catch { close(28); }
        });
      },
    });
    if (behavior.spawnError) setImmediate(() => child.emit('error', new Error('private spawn error')));
    return child;
  }
  return { spawnImpl, calls };
}

test('curl copies exact bytes using private configuration, custom timeout and the same readback checks', async () => {
  const clock = progressClock();
  const f = fixture({ clock, env: { UPLOAD_CLIENT: 'curl', UPLOAD_TIMEOUT_MINUTES: '30', JOB_TIMEOUT_MINUTES: '180' } });
  const curl = curlProcess(f, { onChunk: () => clock.advance(), beforeResponse: () => clock.advance() });
  assert.deepEqual(await f.run({ spawnImpl: curl.spawnImpl }), { uploaded: 1, skipped: 0, verified: 1, commit: COMMIT });
  assert.equal(curl.calls.length, 1);
  const call = curl.calls[0];
  assert.ok(['curl', 'curl.exe'].includes(call.command));
  assert.deepEqual(call.args, ['--disable', '--config', call.path]);
  assert.equal(call.options.shell, false);
  assert.equal(call.options.windowsHide, true);
  assert.equal(call.options.timeout, 1_805_000);
  assert.equal(call.options.killSignal, 'SIGKILL');
  for (const name of ['GITHUB_TOKEN', 'GH_TOKEN', 'ATOMGIT_TOKEN']) assert.equal(call.options.env[name], undefined);
  for (const flag of ['proto = "=https"', 'max-redirs = "0"', 'retry = "0"', 'max-time = "1800"', 'upload-file = "-"', 'header = "Transfer-Encoding:"'])
    assert.ok(call.config.includes(flag), flag);
  assert.equal(existsSync(call.path), false);
  assert.deepEqual(f.stored.get('StarPie.zip'), Buffer.from('signed original bytes'));
  assert.ok(f.logs.some(line => line.includes('body passed to curl; waiting for server confirmation')));
  assert.match(f.logs.join('\n'), /Upload client: curl; upload timeout: 30 minutes; job timeout: 180 minutes/);
  assert.doesNotMatch(f.logs.join('\n'), /private-|https:\/\/|atomgit-test-secret|github-test-secret/);
  assert.equal(clock.active(), 0);
});

for (const [name, behavior] of [
  ['nonzero exit', { exitCode: 28 }], ['redirect', { output: '307' }],
  ['HTTP failure', { output: '403' }], ['malformed output', { output: '200 private-signed-url' }],
  ['missing executable', { spawnError: true }], ['synchronous spawn failure', { spawnThrow: true }],
  ['broken stdin', { stdinError: true }],
]) {
  test('curl fails safely without retry and clears private state: ' + name, async () => {
    const clock = progressClock();
    const f = fixture({ clock, env: { UPLOAD_CLIENT: 'curl' } });
    const curl = curlProcess(f, behavior);
    await assert.rejects(f.run({ spawnImpl: curl.spawnImpl }), (error) => /Asset upload failed/.test(error.message) && !/private|secret|https:/.test(error.message));
    assert.equal(curl.calls.length, 1);
    assert.equal(existsSync(curl.calls[0].path), false);
    assert.equal(clock.active(), 0);
    assert.doesNotMatch(f.logs.join('\n'), /private|secret|https:\/\//);
    assert.equal(f.calls.filter(call => call.method === 'PUT').length <= 1, true);
  });
}

test('curl still rejects damaged readback and never overwrites the uploaded file', async () => {
  const f = fixture({ env: { UPLOAD_CLIENT: 'curl' }, intercept: ({ url }) => url.host === 'downloads.atomgit.example' ? new Response('wrong bytes') : null });
  const curl = curlProcess(f);
  await assert.rejects(f.run({ spawnImpl: curl.spawnImpl }), /conflicts with GitHub/);
  assert.equal(curl.calls.length, 1);
  assert.equal(writes(f).some(call => ['DELETE', 'PATCH'].includes(call.method)), false);
});

test('curl reuses an already verified attachment without starting an upload process', async () => {
  const f = fixture({ env: { UPLOAD_CLIENT: 'curl' }, existing: true, stored: [['StarPie.zip', Buffer.from('signed original bytes')]] });
  const curl = curlProcess(f);
  assert.equal((await f.run({ spawnImpl: curl.spawnImpl })).skipped, 1);
  assert.equal(curl.calls.length, 0);
  assert.equal(writes(f).length, 0);
});

test('Node uploader uses the configured upload timeout and never starts curl', async (t) => {
  const original = AbortSignal.timeout;
  let timeout;
  t.mock.method(AbortSignal, 'timeout', (ms) => { timeout = ms; return original(ms); });
  const f = fixture({ env: { UPLOAD_TIMEOUT_MINUTES: '25' }, intercept: ({ url }) => {
    if (url.host === 'storage.atomgit.example') assert.equal(timeout, 1_500_000);
  } });
  await f.run({ spawnImpl: () => { throw new Error('Node mode must not start curl'); } });
});

for (const headers of [{ 'x-bad\r\nheader': 'value' }, { 'x-callback': 'value\r\nurl = "https://evil.example"' }]) {
  test('curl rejects control characters in signed headers before spawning: ' + JSON.stringify(headers), async () => {
    const f = fixture({ env: { UPLOAD_CLIENT: 'curl' }, intercept: ({ url }) => url.pathname.endsWith('/upload_url') ? json({ url: 'https://storage.atomgit.example/StarPie.zip', headers }) : null });
    const curl = curlProcess(f);
    await assert.rejects(f.run({ spawnImpl: curl.spawnImpl }), /unsafe upload headers/);
    assert.equal(curl.calls.length, 0);
  });
}

test('curl lost confirmation is not retried and the next run reuses the completed file', async () => {
  const f = fixture({ env: { UPLOAD_CLIENT: 'curl' }, uploadLostResponse: true });
  const curl = curlProcess(f);
  await assert.rejects(f.run({ spawnImpl: curl.spawnImpl }), /Asset upload failed/);
  assert.equal(curl.calls.length, 1);
  assert.equal((await f.run({ spawnImpl: curl.spawnImpl })).skipped, 1);
  assert.equal(curl.calls.length, 1);
  assert.equal(f.calls.filter(call => call.method === 'PUT').length, 1);
});

test('curl keeps signed query strings and escaped callback values out of argv and logs', async () => {
  const f = fixture({ env: { UPLOAD_CLIENT: 'curl' }, intercept: ({ url }) => {
    if (url.pathname.endsWith('/upload_url')) return json({
      url: 'https://storage.atomgit.example/StarPie.zip?signature=private-url-signature',
      headers: { 'x-callback': 'private-callback"with\\quotes' },
    });
  } });
  const curl = curlProcess(f);
  await f.run({ spawnImpl: curl.spawnImpl });
  assert.ok(curl.calls[0].config.includes('private-url-signature'));
  assert.doesNotMatch(curl.calls[0].args.join(' '), /private-|https:\/\//);
  assert.doesNotMatch(f.logs.join('\n'), /private-|https:\/\//);
  const put = f.calls.find(call => call.method === 'PUT');
  assert.equal(put.headers['x-callback'], 'private-callback"with\\quotes');
});
