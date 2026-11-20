import { pathToFileURL } from 'node:url';

function repoPath(name) {
  if (!name || !/^[\w.-]+\/[\w.-]+$/.test(name) || name.split('/').some(part => /^\.+$/.test(part))) {
    throw new Error('Expected an owner/repository identifier.');
  }
  return name;
}

function identifier(value) {
  if (typeof value !== 'string' || !value || /[\x00-\x1f\x7f-\x9f\u2028\u2029]/.test(value)) {
    throw new Error('Invalid release tag or attachment name in API response.');
  }
  return value;
}

function indexBy(items, key) {
  const result = new Map();
  for (const item of items) {
    const name = identifier(item?.[key]);
    if (result.has(name)) throw new Error('Duplicate item in API response; rerun after any release edits finish.');
    result.set(name, item);
  }
  return result;
}

function generatedArchive(asset, repository) {
  // AtomGit includes generated source archives alongside uploaded release attachments.
  if (!asset.browser_download_url || !URL.canParse(asset.browser_download_url)) return false;
  const url = new URL(asset.browser_download_url);
  return ['atomgit.com', 'gitcode.com'].includes(url.hostname)
    && url.pathname.toLowerCase().startsWith('/' + repository.toLowerCase() + '/-/archive/');
}

export async function audit(env, fetchImpl = fetch, output = console.log) {
  const source = repoPath(env.GITHUB_REPOSITORY);
  const target = repoPath(env.ATOMGIT_REPOSITORY || source);
  const secrets = [env.GITHUB_TOKEN, env.ATOMGIT_TOKEN];
  if (secrets.some(token => !token)) throw new Error('Both repository service credentials are required.');
  const log = message => {
    for (const secret of secrets) message = message.replaceAll(secret, '***').replaceAll(encodeURIComponent(secret), '***');
    output(message);
  };

  async function request(service, path) {
    const github = service === 'GitHub';
    const base = github ? 'https://api.github.com/repos/' + source : 'https://api.atomgit.com/api/v5/repos/' + target;
    const url = new URL(base + path);
    const headers = { Accept: 'application/json', 'User-Agent': 'StarPie-Mirror-Audit' };
    if (github) headers.Authorization = 'Bearer ' + env.GITHUB_TOKEN;
    else headers['PRIVATE-TOKEN'] = env.ATOMGIT_TOKEN;
    let response;
    try {
      response = await fetchImpl(url, { headers, redirect: 'error', signal: AbortSignal.timeout(60000) });
    } catch {
      throw new Error(service + ' metadata request failed or timed out.');
    }
    if (!response.ok) throw new Error(service + ' metadata request returned HTTP ' + response.status + '.');
    try { return await response.json(); }
    catch { throw new Error(service + ' returned invalid JSON.'); }
  }

  async function pages(service, path) {
    const items = [];
    const seenMirrorTags = new Set();
    for (let page = 1; page <= 1000; page++) {
      const batch = await request(service, path + '?per_page=100&page=' + page);
      if (!Array.isArray(batch)) throw new Error(service + ' returned an invalid list.');
      if (service === 'AtomGit') {
        if (batch.length === 0) return items;
        for (const entry of batch) {
          const tag = identifier(entry?.tag_name);
          if (seenMirrorTags.has(tag)) throw new Error('Duplicate AtomGit release during pagination.');
          seenMirrorTags.add(tag);
        }
      }
      items.push(...batch);
      if (service === 'GitHub' && batch.length < 100) return items;
    }
    throw new Error('Metadata pagination limit exceeded.');
  }

  const [github, mirrors] = await Promise.all([pages('GitHub', '/releases'), pages('AtomGit', '/releases')]);
  if (github.some(release => typeof release?.draft !== 'boolean')) throw new Error('Invalid GitHub release visibility.');
  const published = indexBy(github.filter(release => !release.draft), 'tag_name');
  const mirrored = indexBy(mirrors.filter(release => release?.draft !== true && release?.release_status !== 'draft'), 'tag_name');
  let missingReleases = 0, missingFiles = 0, missingBytes = 0, mismatches = 0, extraFiles = 0, extraReleases = 0;

  let metadataDifferences = 0, metadataUnavailable = 0;

  for (const [tag, release] of published) {
    if (!Number.isSafeInteger(release.id) || release.id < 1) throw new Error('Invalid GitHub release ID.');
    const expected = indexBy(await pages('GitHub', '/releases/' + release.id + '/assets'), 'name');
    let targetRelease = mirrored.get(tag);
    let actual = new Map();
    if (targetRelease) {
      let assets = targetRelease.assets;
      if (assets == null || (Array.isArray(assets) && !assets.length && expected.size)) {
        const detail = await request('AtomGit', '/releases/tags/' + encodeURIComponent(tag));
        if (detail?.tag_name !== tag) throw new Error('AtomGit returned the wrong release detail.');
        targetRelease = { ...targetRelease, ...detail };
        assets = detail.assets;
      }
      if (!Array.isArray(assets)) throw new Error('AtomGit returned an invalid release attachment list.');
      if (assets.some(asset => !asset || typeof asset !== 'object')) throw new Error('Invalid AtomGit attachment.');
      actual = indexBy(assets.filter(asset => !generatedArchive(asset, target)), 'name');
      const fields = [], unavailable = [];
      if (Object.hasOwn(release, 'name') && typeof targetRelease.name === 'string') {
        if (targetRelease.name !== (release.name || tag)) fields.push('name');
      } else unavailable.push('name');
      if (Object.hasOwn(release, 'body') && typeof targetRelease.body === 'string') {
        const sourceBody = (release.body || '').replaceAll('\r\n', '\n');
        let mirrorBody = targetRelease.body.replaceAll('\r\n', '\n');
        if (source.toLowerCase() === 'star-pie/starpie-official-plugins') {
          // The plugin client consumes the source publication time appended by its release workflow.
          const marker = /\n\n<!-- starpie-source-published-at: ([^\s]+) -->$/.exec(mirrorBody);
          if (marker) mirrorBody = mirrorBody.slice(0, marker.index);
          if (release.published_at) {
            if (!marker || Date.parse(marker[1]) !== Date.parse(release.published_at)) fields.push('catalog timestamp');
          } else unavailable.push('catalog timestamp');
        }
        if (mirrorBody !== sourceBody) fields.push('body');
      } else unavailable.push('body');
      if (typeof release.prerelease === 'boolean' && typeof targetRelease.prerelease === 'boolean') {
        if (targetRelease.prerelease !== release.prerelease) fields.push('prerelease');
      } else unavailable.push('prerelease');
      if (fields.length) {
        metadataDifferences++;
        log('RELEASE METADATA MISMATCH ' + JSON.stringify(tag) + ' (' + fields.join(', ') + ')');
      }
      if (unavailable.length) {
        metadataUnavailable++;
        log('RELEASE FIELDS UNAVAILABLE ' + JSON.stringify(tag) + ' (' + unavailable.join(', ') + ')');
      }
    } else {
      missingReleases++;
      log('MISSING release ' + JSON.stringify(tag));
    }

    for (const [name, file] of expected) {
      if (!Number.isSafeInteger(file.size) || file.size < 0) throw new Error('Invalid GitHub asset size.');
      const mirroredFile = actual.get(name);
      const label = JSON.stringify(tag) + ' ' + JSON.stringify(name);
      if (!mirroredFile) {
        missingFiles++;
        missingBytes += file.size;
        log('MISSING attachment ' + label + ' (' + file.size + ' bytes)');
      } else if ((mirroredFile.size != null && mirroredFile.size !== file.size)
        || (mirroredFile.digest && file.digest && mirroredFile.digest !== file.digest)) {
        mismatches++;
        log('METADATA MISMATCH attachment ' + label);
      }
    }
    for (const name of actual.keys()) {
      if (!expected.has(name)) {
        extraFiles++;
        log('EXTRA attachment ' + JSON.stringify(tag) + ' ' + JSON.stringify(name));
      }
    }
  }
  for (const tag of mirrored.keys()) {
    if (!published.has(tag)) {
      extraReleases++;
      log('EXTRA release ' + JSON.stringify(tag));
    }
  }
  log('Published releases: GitHub=' + published.size + ', AtomGit=' + mirrored.size
    + '. Missing releases=' + missingReleases + ', missing attachments=' + missingFiles
    + ' (' + missingBytes + ' bytes), mismatched attachments=' + mismatches
    + ', releases with metadata differences=' + metadataDifferences + ', releases with unavailable fields=' + metadataUnavailable
    + ', extra releases=' + extraReleases + ', extra attachments=' + extraFiles + '.');
  return missingReleases + missingFiles + mismatches + metadataDifferences + extraReleases + extraFiles === 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try { if (!await audit(process.env)) process.exitCode = 1; }
  catch (error) { console.error('Release inventory incomplete. ' + error.message); process.exitCode = 1; }
}
