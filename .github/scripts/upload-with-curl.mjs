import { spawn } from 'node:child_process';
import { createReadStream } from 'node:fs';
import { writeFile, rm } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import { join } from 'node:path';
import { Transform } from 'node:stream';
import { pipeline } from 'node:stream/promises';

function quoted(value) {
  return '"' + String(value).replaceAll('\\', '\\\\').replaceAll('"', '\\"') + '"';
}

// URL and callback headers stay in a private temporary file, never in argv or logs.
export async function uploadWithCurl({ url, headers, source, workDirectory, timeoutMs, progress, spawnImpl = spawn }) {
  const configPath = join(workDirectory, 'curl-' + randomUUID() + '.conf');
  const lines = [
    'url = ' + quoted(url), 'request = "PUT"', 'upload-file = "-"',
    'output = ' + quoted(process.platform === 'win32' ? 'NUL' : '/dev/null'),
    'silent', 'globoff', 'proto = "=https"', 'max-redirs = "0"', 'retry = "0"',
    ...(timeoutMs === null ? [] : ['connect-timeout = "60"', 'max-time = ' + quoted(timeoutMs / 1000)]),
    'write-out = "%{response_code}"',
    // stdin has no discoverable file size; suppress curl's automatic chunked framing.
    'header = ' + quoted('Content-Length: ' + source.size), 'header = "Transfer-Encoding:"',
    ...Object.entries(headers).filter(([name]) => !['content-length', 'transfer-encoding'].includes(name.toLowerCase()))
      .map(([name, value]) => 'header = ' + quoted(name + ': ' + value)),
  ];
  let child;
  let input;
  let meter;
  let pumping;
  let completion;
  let closed = false;
  let status = '';
  try {
    await writeFile(configPath, lines.join('\n') + '\n', { mode: 0o600, flag: 'wx' });
    const env = { ...process.env };
    for (const name of ['GITHUB_TOKEN', 'GH_TOKEN', 'ATOMGIT_TOKEN']) delete env[name];
    // Disable user curlrc, shell expansion, implicit retries and redirect following.
    child = spawnImpl(process.platform === 'win32' ? 'curl.exe' : 'curl', ['--disable', '--config', configPath], {
      shell: false, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'], env,
      // Zero disables the parent timer; oversized timers must not wrap to 1 ms.
      timeout: timeoutMs !== null && timeoutMs + 5_000 <= 2_147_483_647 ? timeoutMs + 5_000 : 0,
      killSignal: 'SIGKILL',
    });
    completion = new Promise((resolve, reject) => {
      child.once('error', reject);
      child.once('close', (code) => { closed = true; resolve(code); });
    });
    child.stdout.on('data', (chunk) => { status = (status + chunk.toString()).slice(0, 32); });
    // curl errors can contain signed URLs or callback headers. Discard, never forward.
    child.stderr.resume();
    input = createReadStream(source.path);
    meter = new Transform({ transform(chunk, encoding, callback) {
      progress.add(chunk.length);
      callback(null, chunk);
    } });
    pumping = pipeline(input, meter, child.stdin).then(() => {
      progress.phase('body passed to curl; waiting for server confirmation', true);
    });
    const [code] = await Promise.all([completion, pumping]);
    if (code !== 0 || !/^2\d\d$/.test(status)) throw new Error('curl upload was not confirmed.');
  } catch {
    throw new Error('curl upload failed or was not confirmed.');
  } finally {
    if (child && !closed) child.kill('SIGKILL');
    input?.destroy();
    meter?.destroy();
    child?.stdin.destroy();
    await Promise.allSettled([pumping, completion]);
    await rm(configPath, { force: true });
  }
}
