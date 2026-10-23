import { createHash } from 'node:crypto';
import { createReadStream, createWriteStream } from 'node:fs';
import { appendFile, mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { Readable, Transform } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { pathToFileURL } from 'node:url';

const GITHUB_API = 'https://api.github.com';
const ATOMGIT_API = 'https://api.atomgit.com';
const RETRYABLE = new Set([429, 500, 502, 503, 504]);
const pause = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function requireValue(value, name) {
  if (typeof value !== 'string' || !value.trim()) throw new Error(`${name} is required.`);
  return value.trim();
}

function repositoryPath(value) {
  const parts = value.split('/');
  if (parts.length !== 2 || parts.some((part) => !/^[A-Za-z0-9_.-]+$/.test(part) || /^\.+$/.test(part))) {
    throw new Error('Repository must have the form owner/repository.');
  }
  return parts.map(encodeURIComponent).join('/');
}

export function configuration(env) {
  const tag = requireValue(env.RELEASE_TAG, 'RELEASE_TAG');
  if (!/^[A-Za-z0-9][A-Za-z0-9._+/-]{0,199}$/.test(tag) || /\.\.|\/\/|\.lock(?:\/|$)|[/.]$/.test(tag)) {
    throw new Error('RELEASE_TAG is not a supported Git tag.');
  }
  const repository = requireValue(env.GITHUB_REPOSITORY, 'GITHUB_REPOSITORY');
  const target = env.ATOMGIT_REPOSITORY || repository;
  repositoryPath(repository);
  repositoryPath(target);
  return {
    tag, repository, target,
    githubToken: requireValue(env.GITHUB_TOKEN, 'GITHUB_TOKEN'),
    atomgitToken: requireValue(env.ATOMGIT_TOKEN, 'ATOMGIT_TOKEN'),
    summaryPath: env.GITHUB_STEP_SUMMARY,
    tagAttempts: 30,
    tagIntervalMs: 60_000,
    visibilityAttempts: 6,
    visibilityIntervalMs: 5_000,
  };
}

function sha(value) {
  if (!/^[a-f0-9]{40}$/i.test(value || '')) throw new Error('API returned an invalid commit SHA.');
  return value.toLowerCase();
}

function indexAssets(assets) {
  if (!Array.isArray(assets)) throw new Error('API returned an invalid asset list.');
  const result = new Map();
  for (const asset of assets) {
    if (typeof asset.name !== 'string' || !asset.name || /[\\/\x00-\x1f\x7f]/.test(asset.name)) {
      throw new Error('API returned an unsafe asset name.');
    }
    if (result.has(asset.name)) throw new Error('API returned duplicate asset names.');
    result.set(asset.name, asset);
  }
  return result;
}

export async function syncRelease(config, { fetchImpl = fetch, sleep = pause, log = console.log } = {}) {
  const githubBase = `/repos/${repositoryPath(config.repository)}`;
  const atomgitBase = `/api/v5/repos/${repositoryPath(config.target)}`;
  const tag = encodeURIComponent(config.tag);
  const secrets = [config.githubToken, config.atomgitToken].filter(Boolean);
  const workDirectory = await mkdtemp(join(tmpdir(), 'starpie-atomgit-'));

  function secureUrl(value) {
    let url;
    try { url = new URL(value); } catch { throw new Error('API returned an invalid transfer URL.'); }
    if (url.protocol !== 'https:' || url.username || url.password || secrets.some((secret) => url.href.includes(secret))) {
      throw new Error('API returned an unsafe transfer URL.');
    }
    return url;
  }

  // Never log remote bodies, signed URLs, fetch exceptions or request headers: any can contain credentials.
  async function requestJson(service, path, { method = 'GET', body, missing = false } = {}) {
    const url = new URL(path, service === 'github' ? GITHUB_API : ATOMGIT_API);
    const headers = { Accept: 'application/json', 'User-Agent': 'StarPie-Release-Mirror' };
    if (service === 'github') {
      headers.Authorization = `Bearer ${config.githubToken}`;
      headers['X-GitHub-Api-Version'] = '2022-11-28';
    } else {
      // Release upload/create APIs document access_token as a query parameter.
      url.searchParams.set('access_token', config.atomgitToken);
    }
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const attempts = method === 'GET' ? 3 : 1;
    for (let attempt = 1; attempt <= attempts; attempt++) {
      let response;
      try {
        response = await fetchImpl(url, {
          method, headers, body: body === undefined ? undefined : JSON.stringify(body),
          redirect: 'manual', signal: AbortSignal.timeout(60_000),
        });
      } catch {
        if (attempt < attempts) { await sleep(attempt * 2_000); continue; }
        throw new Error(`${service} ${method} request failed or timed out; rerun the synchronization workflow.`);
      }
      if (missing && response.status === 404) { await response.body?.cancel(); return null; }
      if (RETRYABLE.has(response.status) && attempt < attempts) {
        await response.body?.cancel();
        await sleep(attempt * 2_000);
        continue;
      }
      if (!response.ok) {
        await response.body?.cancel();
        throw new Error(`${service} ${method} returned HTTP ${response.status}; check token permissions, service status and tag, then rerun.`);
      }
      try { return await response.json(); }
      catch { throw new Error(`${service} returned invalid JSON.`); }
    }
  }

  async function download(urlValue, path, headers = {}) {
    let url = secureUrl(urlValue);
    let requestHeaders = headers;
    for (let redirects = 0; redirects <= 5; redirects++) {
      let response;
      try {
        response = await fetchImpl(url, {
          headers: requestHeaders, redirect: 'manual', signal: AbortSignal.timeout(600_000),
        });
      } catch { throw new Error('Asset download failed or timed out; rerun the synchronization workflow.'); }
      if ([301, 302, 303, 307, 308].includes(response.status)) {
        await response.body?.cancel();
        const location = response.headers.get('location');
        if (!location) throw new Error('Asset redirect is missing its destination.');
        const next = secureUrl(new URL(location, url).href);
        if (next.origin !== url.origin) requestHeaders = {};
        url = next;
        continue;
      }
      if (!response.ok || !response.body) {
        await response.body?.cancel();
        throw new Error(`Asset download returned HTTP ${response.status}.`);
      }
      const hash = createHash('sha256');
      let size = 0;
      const meter = new Transform({ transform(chunk, encoding, callback) {
        size += chunk.length;
        hash.update(chunk);
        callback(null, chunk);
      } });
      try {
        await pipeline(Readable.fromWeb(response.body), meter, createWriteStream(path));
      } catch { throw new Error('Asset download stream failed; rerun the synchronization workflow.'); }
      return { size, digest: hash.digest('hex') };
    }
    throw new Error('Asset download exceeded the redirect limit.');
  }

  async function atomgitTagCommit() {
    for (let page = 1; page <= 100; page++) {
      const tags = await requestJson('atomgit', `${atomgitBase}/tags?per_page=100&page=${page}`);
      if (!Array.isArray(tags)) throw new Error('AtomGit returned an invalid tag list.');
      const found = tags.find((entry) => entry.name === config.tag);
      if (found) return sha(found.commit?.sha);
      if (tags.length < 100) return null;
    }
    throw new Error('AtomGit tag pagination exceeded its safety limit.');
  }

  function checkRelease(release, commit) {
    if (release.tag_name !== config.tag || sha(release.target_commitish) !== commit) {
      throw new Error('AtomGit release tag/commit differs from GitHub; no tag or release will be overwritten.');
    }
  }

  const releasePath = `${atomgitBase}/releases/tags/${tag}`;
  try {
    const release = await requestJson('github', `${githubBase}/releases/tags/${tag}`);
    if (release.draft || release.tag_name !== config.tag || !release.published_at) {
      throw new Error('Only an already-published GitHub release with the exact requested tag can be mirrored.');
    }
    // Release assets have their own pagination; the embedded list need not be complete.
    const sourceAssets = [];
    if (!Number.isSafeInteger(release.id) || release.id <= 0) throw new Error('GitHub returned an invalid release ID.');
    for (let page = 1; page <= 100; page++) {
      const assets = await requestJson('github', `${githubBase}/releases/${release.id}/assets?per_page=100&page=${page}`);
      if (!Array.isArray(assets)) throw new Error('GitHub returned an invalid asset list.');
      sourceAssets.push(...assets);
      if (assets.length < 100) break;
      if (page === 100) throw new Error('GitHub asset pagination exceeded its safety limit.');
    }
    const sourceIndex = indexAssets(sourceAssets);
    if (sourceIndex.size === 0) throw new Error('GitHub release has no uploaded assets; refusing to mirror an incomplete release.');
    let ref = await requestJson('github', `${githubBase}/git/ref/tags/${tag}`);
    for (let depth = 0; ref.object?.type === 'tag' && depth < 10; depth++) {
      ref = await requestJson('github', `${githubBase}/git/tags/${sha(ref.object.sha)}`);
    }
    if (ref.object?.type !== 'commit') throw new Error('GitHub tag did not resolve to a commit.');
    const commit = sha(ref.object.sha);
    let mirrored = false;
    for (let attempt = 1; attempt <= config.tagAttempts; attempt++) {
      const targetCommit = await atomgitTagCommit();
      if (targetCommit !== null) {
        if (targetCommit !== commit) throw new Error('AtomGit tag points to a different commit; refusing to publish.');
        mirrored = true;
        break;
      }
      if (attempt < config.tagAttempts) {
        log(`Waiting for AtomGit Pull mirror (${attempt}/${config.tagAttempts}).`);
        await sleep(config.tagIntervalMs);
      }
    }
    if (!mirrored) throw new Error('AtomGit tag is not mirrored yet. Synchronize the repository mirror and rerun this workflow.');

    // Validate/download every source file before the first remote write.
    const files = new Map();
    let sequence = 0;
    for (const asset of sourceIndex.values()) {
      if (!Number.isSafeInteger(asset.id) || asset.id <= 0 || !Number.isSafeInteger(asset.size) || asset.size < 0 || asset.state !== 'uploaded') {
        throw new Error('GitHub returned an incomplete or invalid asset.');
      }
      const path = join(workDirectory, `source-${sequence++}`);
      const actual = await download(`${GITHUB_API}${githubBase}/releases/assets/${asset.id}`, path, {
        Authorization: `Bearer ${config.githubToken}`, Accept: 'application/octet-stream',
        'X-GitHub-Api-Version': '2022-11-28',
      });
      if (actual.size !== asset.size || (asset.digest && asset.digest !== `sha256:${actual.digest}`)) {
        throw new Error(`GitHub asset integrity check failed: ${JSON.stringify(asset.name)}.`);
      }
      files.set(asset.name, { ...actual, path });
    }

    let targetRelease = await requestJson('atomgit', releasePath, { missing: true });
    if (targetRelease) checkRelease(targetRelease, commit);
    const metadata = { name: release.name || release.tag_name, body: release.body || '' };
    // AtomGit exposes pre/latest, not GitHub's independent make_latest flag.
    // Do not explicitly promote an older stable GitHub release to latest.
    if (release.prerelease) metadata.release_status = 'pre';
    else {
      const latest = await requestJson('github', `${githubBase}/releases/latest`, { missing: true });
      if (latest?.id === release.id) metadata.release_status = 'latest';
    }
    async function verifyAsset(name, existing, source) {
      const actual = await download(existing.browser_download_url, join(workDirectory, 'target-verification'));
      if (actual.size !== source.size || actual.digest !== source.digest) {
        throw new Error(`AtomGit asset conflicts with GitHub: ${JSON.stringify(name)}. Nothing was deleted or overwritten.`);
      }
      log(`Verified SHA-256: ${JSON.stringify(name)}.`);
    }
    // Check ALL existing names before uploading even one missing file or changing metadata.
    const existingIndex = indexAssets(targetRelease?.assets || []);
    for (const [name, source] of files) {
      if (existingIndex.has(name)) await verifyAsset(name, existingIndex.get(name), source);
    }
    if (!targetRelease) {
      await requestJson('atomgit', `${atomgitBase}/releases`, {
        method: 'POST', body: { ...metadata, tag_name: config.tag, target_commitish: commit },
      });
      targetRelease = await requestJson('atomgit', releasePath);
      checkRelease(targetRelease, commit);
    }
    let targetIndex = indexAssets(targetRelease.assets);
    let uploaded = 0;
    let skipped = 0;
    for (const [name, source] of files) {
      let existing = targetIndex.get(name);
      if (!existing) {
        const upload = await requestJson('atomgit', `${atomgitBase}/releases/${tag}/upload_url?file_name=${encodeURIComponent(name)}`);
        const url = secureUrl(upload.url);
        if (!upload.headers || typeof upload.headers !== 'object' || Array.isArray(upload.headers)) {
          throw new Error('AtomGit returned invalid upload headers.');
        }
        for (const value of Object.values(upload.headers)) {
          if (typeof value !== 'string' || secrets.some((secret) => value.includes(secret))) {
            throw new Error('AtomGit returned unsafe upload headers.');
          }
        }
        const stream = createReadStream(source.path);
        let response;
        try {
          response = await fetchImpl(url, {
            method: 'PUT', headers: { ...upload.headers, 'Content-Length': String(source.size) },
            body: stream, duplex: 'half', redirect: 'manual', signal: AbortSignal.timeout(600_000),
          });
          if (!response.ok) throw new Error();
          await response.body?.cancel();
        } catch { throw new Error('Asset upload failed or timed out. Rerun to verify/reuse any completed upload; no file will be overwritten automatically.'); }
        finally { stream.destroy(); }
        uploaded++;
        // Object storage callbacks can become visible later than the PUT response.
        for (let attempt = 1; attempt <= config.visibilityAttempts; attempt++) {
          targetRelease = await requestJson('atomgit', releasePath);
          checkRelease(targetRelease, commit);
          targetIndex = indexAssets(targetRelease.assets);
          existing = targetIndex.get(name);
          if (existing) break;
          if (attempt < config.visibilityAttempts) await sleep(config.visibilityIntervalMs);
        }
        if (!existing) throw new Error('Uploaded attachment is not visible yet. Wait, then rerun the workflow.');
        await verifyAsset(name, existing, source);
      } else skipped++;
    }
    // Update title/notes only after every attachment has passed verification.
    if (targetRelease.name !== metadata.name || targetRelease.body !== metadata.body ||
        (metadata.release_status && targetRelease.release_status !== metadata.release_status)) {
      await requestJson('atomgit', `${atomgitBase}/releases/${tag}`, { method: 'PATCH', body: metadata });
    }
    const final = await requestJson('atomgit', releasePath);
    checkRelease(final, commit);
    const finalIndex = indexAssets(final.assets);
    if (final.name !== metadata.name || final.body !== metadata.body ||
        Boolean(final.prerelease) !== Boolean(release.prerelease) ||
        (metadata.release_status && final.release_status !== metadata.release_status) ||
        [...files.keys()].some((name) => !finalIndex.has(name))) {
      throw new Error('AtomGit final release verification failed; rerun the workflow.');
    }
    const result = { uploaded, skipped, verified: files.size, commit };
    log(`AtomGit sync complete: ${uploaded} uploaded, ${skipped} reused, ${files.size} verified.`);
    if (config.summaryPath) {
      await appendFile(config.summaryPath,
        `## AtomGit release synchronization\n\nCommit: \`${commit}\`\n\nUploaded: ${uploaded}; reused: ${skipped}; SHA-256 verified: ${files.size}.\n`);
    }
    return result;
  } finally {
    // This directory is created by mkdtemp above, never derived from a tag or filename.
    await rm(workDirectory, { recursive: true, force: true });
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  try { await syncRelease(configuration(process.env)); }
  catch (error) {
    // All runtime errors above are deliberately credential-free; avoid nested fetch causes.
    console.error(error.message);
    process.exitCode = 1;
  }
}
