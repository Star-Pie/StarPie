import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { spawn } from 'node:child_process';
import { mkdtempSync, writeFileSync, readFileSync, readdirSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname, resolve, basename } from 'node:path';
import { uploadWithCurl } from './upload-with-curl.mjs';

// The only real network endpoint in these tests is an ephemeral loopback server.
async function loopback(t, status = 200) {
  const directory = mkdtempSync(join(tmpdir(), 'starpie-curl-test-'));
  const content = Buffer.alloc(300000, 97);
  const source = { path: join(directory, 'payload.bin'), size: content.length };
  writeFileSync(source.path, content);
  let requests = 0;
  let failure;
  let child;
  const callback = 'quote"and\\backslash';
  const server = createServer(async (request, response) => {
    requests++;
    try {
      const chunks = [];
      for await (const chunk of request) chunks.push(chunk);
      assert.equal(request.method, 'PUT');
      assert.equal(request.headers['content-length'], String(content.length));
      assert.equal(request.headers['transfer-encoding'], undefined);
      assert.equal(request.headers['x-callback'], callback);
      assert.deepEqual(Buffer.concat(chunks), content);
      if (status === 'stall') return;
      response.writeHead(status, status === 302 ? { location: '/must-not-follow' } : {});
      response.end();
    } catch (error) { failure = error; response.writeHead(500); response.end(); }
  });
  t.after(async () => {
    server.closeAllConnections();
    await new Promise(ok => server.close(ok));
    assert.equal(dirname(resolve(directory)), resolve(tmpdir()));
    assert.ok(basename(directory).startsWith('starpie-curl-test-'));
    rmSync(directory, { recursive: true, force: true });
  });
  await new Promise((ok, fail) => { server.once('error', fail); server.listen(0, '127.0.0.1', ok); });
  let bytes = 0;
  const spawnImpl = (command, args, options) => {
    assert.equal(dirname(args[2]), directory);
    const config = readFileSync(args[2], 'utf8');
    assert.ok(config.includes('proto = "=https"'));
    // Test-only mapping. Never connect to the placeholder signed URL or a release API.
    const local = config.replace(/^url = .+$/m, 'url = "http://127.0.0.1:' + server.address().port + '/sink"')
      .replace('proto = "=https"', 'proto = "=http"') + '\nnoproxy = "*"\n';
    writeFileSync(args[2], local);
    child = spawn(command, args, options);
    return child;
  };
  return {
    upload: () => uploadWithCurl({ url: 'https://not-contacted.invalid/object?signature=test-only',
      headers: { 'x-callback': callback }, source, workDirectory: directory,
      timeoutMs: status === 'stall' ? 2000 : 5000,
      progress: { add: count => { bytes += count; }, phase() {} }, spawnImpl }),
    verify: () => {
      if (failure) throw failure;
      assert.equal(requests, 1);
      assert.equal(bytes, content.length);
      assert.deepEqual(readdirSync(directory), ['payload.bin']);
      assert.notEqual(child.exitCode, null);
    },
  };
}

test('real curl sends fixed Content-Length and exact bytes with escaped callback headers', async (t) => {
  const f = await loopback(t);
  await f.upload();
  f.verify();
});

test('real curl rejects redirects without a second PUT and cleans its private config', async (t) => {
  const f = await loopback(t, 302);
  await assert.rejects(f.upload(), /not confirmed/);
  f.verify();
});

test('real curl max-time stops a stalled response and cleans its process and config', async (t) => {
  const f = await loopback(t, 'stall');
  await assert.rejects(f.upload(), /not confirmed/);
  f.verify();
});
