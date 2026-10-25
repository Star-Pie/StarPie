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

const SAFE_SIGNALS = new Set(['SIGHUP', 'SIGINT', 'SIGQUIT', 'SIGABRT', 'SIGKILL', 'SIGSEGV', 'SIGPIPE', 'SIGTERM']);
const SAFE_ERROR_CODES = new Set([
  'ENOENT', 'EACCES', 'EPERM', 'EPIPE', 'ECONNRESET', 'ECONNABORTED', 'ETIMEDOUT',
  'ENOSPC', 'EIO', 'ENOMEM', 'ABORT_ERR', 'ERR_STREAM_PREMATURE_CLOSE',
  'ERR_STREAM_DESTROYED', 'ERR_STREAM_WRITE_AFTER_END',
]);

// Only bounded numbers and allowlisted labels may leave the transport boundary.
export class CurlUploadError extends Error {
  constructor({ exitCode, status, signal, error, phase, spawnFailed, cleanupKill }) {
    const safeExit = Number.isInteger(exitCode) && exitCode >= 0 && exitCode <= 255 ? exitCode : 'unknown';
    const safeStatus = typeof status === 'string' && status.length === 3 && /^(?:000|[1-5]\d{2})$/.test(status) ? status : 'unknown';
    const safeSignal = signal == null ? 'none' : SAFE_SIGNALS.has(signal) ? signal : 'unknown';
    const safeCode = error == null ? 'none' : SAFE_ERROR_CODES.has(error.code) ? error.code : 'unknown';
    const category = safeExit === 28 || safeCode === 'ETIMEDOUT' ? 'timeout'
      : phase === 'spawn' || spawnFailed ? 'spawn'
      : Number.isInteger(safeExit) && safeExit !== 0 ? 'process'
      : safeStatus !== 'unknown' && safeStatus !== '000' && !/^2\d\d$/.test(safeStatus) ? 'http'
      : phase === 'setup' ? 'setup'
      : error != null ? 'stream'
      : safeSignal !== 'none' ? 'signal' : 'unconfirmed';
    super(`curl upload failed or was not confirmed; category=${category}; exit_code=${safeExit}; http_status=${safeStatus}; signal=${safeSignal}; error_code=${safeCode}; cleanup_kill=${cleanupKill === true}.`);
    this.name = 'CurlUploadError';
  }
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
  let closing;
  let exitCode;
  let signal;
  let processError;
  let failure;
  let failed = false;
  let phase = 'setup';
  let cleanupKill = false;
  let closed = false;
  let status = '';
  try {
    await writeFile(configPath, lines.join('\n') + '\n', { mode: 0o600, flag: 'wx' });
    const env = { ...process.env };
    for (const name of ['GITHUB_TOKEN', 'GH_TOKEN', 'ATOMGIT_TOKEN']) delete env[name];
    // Disable user curlrc, shell expansion, implicit retries and redirect following.
    phase = 'spawn';
    child = spawnImpl(process.platform === 'win32' ? 'curl.exe' : 'curl', ['--disable', '--config', configPath], {
      shell: false, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'], env,
      // Zero disables the parent timer; oversized timers must not wrap to 1 ms.
      timeout: timeoutMs !== null && timeoutMs + 5_000 <= 2_147_483_647 ? timeoutMs + 5_000 : 0,
      killSignal: 'SIGKILL',
    });
    let resolveClose;
    closing = new Promise(resolve => { resolveClose = resolve; });
    completion = new Promise((resolve, reject) => {
      child.once('error', (error) => { processError = error; reject(error); });
      child.once('close', (code, terminationSignal) => {
        closed = true;
        exitCode = code;
        signal = terminationSignal;
        resolve(code);
        resolveClose();
      });
    });
    child.stdout.on('data', (chunk) => { status = (status + chunk.toString()).slice(0, 32); });
    // curl errors can contain signed URLs or callback headers. Discard, never forward.
    child.stderr.resume();
    phase = 'transfer';
    input = createReadStream(source.path);
    meter = new Transform({ transform(chunk, encoding, callback) {
      progress.add(chunk.length);
      callback(null, chunk);
    } });
    pumping = pipeline(input, meter, child.stdin).then(() => {
      progress.phase('body passed to curl; waiting for server confirmation', true);
    });
    const [code] = await Promise.all([completion, pumping]);
    failed = code !== 0 || !/^2\d\d$/.test(status);
  } catch (error) {
    failed = true;
    failure = error;
  } finally {
    if (child && !closed) { cleanupKill = true; child.kill('SIGKILL'); }
    input?.destroy();
    meter?.destroy();
    child?.stdin.destroy();
    await Promise.allSettled([pumping, completion, closing]);
    await rm(configPath, { force: true });
  }
  if (failed) throw new CurlUploadError({
    exitCode, status, signal, error: processError ?? failure, phase,
    spawnFailed: processError !== undefined, cleanupKill,
  });
}
