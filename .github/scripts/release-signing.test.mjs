import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { readFileSync, writeFileSync, mkdtempSync, mkdirSync, copyFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { randomUUID } from 'node:crypto';
import { spawnSync } from 'node:child_process';

const root = fileURLToPath(new URL('../../', import.meta.url));
const workflow = readFileSync(join(root, '.github/workflows/release.yml'), 'utf8').replaceAll('\r\n', '\n');
const scratch = mkdtempSync(join(tmpdir(), 'starpie-release-signing-'));
after(() => {
  assert.equal(dirname(resolve(scratch)), resolve(tmpdir()));
  assert.ok(basename(scratch).startsWith('starpie-release-signing-'));
  rmSync(scratch, { recursive: true, force: true });
});
const starts = [...workflow.matchAll(/^      - name: (.+)$/gm)];
const steps = new Map(starts.map((match, index) => [match[1], workflow.slice(match.index, starts[index + 1]?.index ?? workflow.length)]));
function step(name) { assert.ok(steps.has(name), `Missing workflow step: ${name}`); return steps.get(name); }
function condition(name) { return step(name).match(/^        if: \$\{\{ (.+) \}\}$/m)?.[1]; }
function script(name) {
  const content = step(name).split('        run: |\n')[1];
  assert.ok(content, `Missing PowerShell script: ${name}`);
  const lines = [];
  for (const line of content.split('\n')) {
    if (line.trim() && !line.startsWith('          ')) break;
    lines.push(line.startsWith('          ') ? line.slice(10) : '');
  }
  return lines.join('\n');
}

// Deliberately small, fail-closed evaluator for the policy expressions used here.
// This executes no repository-supplied JavaScript or arbitrary workflow functions.
function evaluate(expression, mode, tag = 'v2.0.1-beta.1', token = '') {
  const tokens = [];
  const pattern = /\s*(env\.SIGNING_POLICY|env\.RELEASE_TAG|secrets\.SIGNPATH_API_TOKEN|contains|&&|\|\||==|!=|\(|\)|,|'[^']*')/y;
  let position = 0;
  while (position < expression.length) {
    if (!expression.slice(position).trim()) break;
    pattern.lastIndex = position;
    const match = pattern.exec(expression);
    assert.ok(match, `Unsupported policy expression: ${expression.slice(position)}`);
    tokens.push(match[1]); position = pattern.lastIndex;
  }
  let index = 0;
  function take(expected) { assert.equal(tokens[index++], expected); }
  function value() {
    const tokenValue = tokens[index++];
    if (tokenValue === 'env.SIGNING_POLICY') return mode;
    if (tokenValue === 'env.RELEASE_TAG') return tag;
    if (tokenValue === 'secrets.SIGNPATH_API_TOKEN') return token;
    assert.match(tokenValue || '', /^'[^']*'$/);
    return tokenValue.slice(1, -1);
  }
  function primary() {
    if (tokens[index] === '(') { take('('); const result = or(); take(')'); return result; }
    if (tokens[index] === 'contains') {
      take('contains'); take('('); const text = value(); take(','); const part = value(); take(')');
      return text.toLowerCase().includes(part.toLowerCase());
    }
    const left = value();
    if (tokens[index] === '==' || tokens[index] === '!=') {
      const operator = tokens[index++], right = value();
      const equal = left.toLowerCase() === right.toLowerCase();
      return operator === '==' ? equal : !equal;
    }
    return left;
  }
  function and() { let result = primary(); while (tokens[index] === '&&') { take('&&'); const right = primary(); result = result ? right : result; } return result; }
  function or() { let result = and(); while (tokens[index] === '||') { take('||'); const right = and(); result = result ? result : right; } return result; }
  const result = or(); assert.equal(index, tokens.length); return result;
}
function runs(name, mode, tag) { const value = condition(name); return value === undefined || Boolean(evaluate(value, mode, tag)); }
function powershell(body, { cwd = root, mode = 'unsigned', tag = 'v2.0.1-beta.1', token = '', env = {} } = {}) {
  const envFile = join(scratch, `${randomUUID()}.env`);
  const result = spawnSync(process.platform === 'win32' ? 'pwsh.exe' : 'pwsh',
    ['-NoProfile', '-NonInteractive', '-EncodedCommand', Buffer.from(`$ErrorActionPreference = 'Stop'\n${body}`, 'utf16le').toString('base64')],
    { cwd, encoding: 'utf8', timeout: 30_000, env: {
      ...process.env, SIGNING_POLICY: mode, SIGNPATH_API_TOKEN: token,
      REQUESTED_TAG: tag, RELEASE_TAG: tag, GITHUB_EVENT_NAME: 'workflow_dispatch',
      GITHUB_ENV: envFile, GITHUB_STEP_SUMMARY: join(scratch, `${randomUUID()}.summary`), ...env,
    } });
  if (result.error) throw result.error;
  return { status: result.status, output: `${result.stdout}\n${result.stderr}`, envFile };
}
const signingSteps = [
  'Prepare single-file signing inputs and verification helper',
  'Upload unsigned Lightweight executable', 'Sign Lightweight executable with SignPath', 'Verify and install signed Lightweight executable',
  'Upload unsigned Standalone executable', 'Sign Standalone executable with SignPath', 'Verify and install signed Standalone executable',
  'Upload unsigned installer executable', 'Sign installer executable with SignPath', 'Verify and install signed installer executable',
];

test('unsigned skips all signing inputs/actions/verifiers; both signed modes retain them', () => {
  for (const name of signingSteps) {
    assert.equal(runs(name, 'unsigned'), false, name);
    assert.equal(runs(name, 'test-signing'), true, name);
    assert.equal(runs(name, 'release-signing'), true, name);
  }
});
test('unsigned and production signing preserve branch/version and publication paths', () => {
  for (const mode of ['unsigned', 'release-signing']) {
    for (const name of ['Verify tag points to latest commit of an allowed release branch', 'Verify compiled version fields',
      'Package release assets', 'Extract Release Notes from CHANGELOG.md', 'Create GitHub Release', 'Retain latest three beta releases'])
      assert.equal(runs(name, mode), true, `${mode}: ${name}`);
    assert.equal(runs('Retain latest three beta releases', mode, 'v2.0.1'), false);
  }
  for (const name of ['Verify tag points to latest commit of an allowed release branch', 'Extract Release Notes from CHANGELOG.md', 'Create GitHub Release', 'Retain latest three beta releases'])
    assert.equal(runs(name, 'test-signing'), false, name);
});
test('test-signing remains artifacts only, and unsigned has its own verification and summary', () => {
  for (const mode of ['test-signing', 'release-signing', 'unsigned']) {
    assert.equal(runs('Save test-signed packages (not for distribution)', mode), mode === 'test-signing');
    assert.equal(runs('Summarize test signing', mode), mode === 'test-signing');
    assert.equal(runs('Verify unsigned executables', mode), mode === 'unsigned');
    assert.equal(runs('Summarize unsigned mode', mode), mode === 'unsigned');
  }
});
test('the validation step does not receive the SignPath secret in unsigned mode', () => {
  const expression = step('Validate signing configuration').match(/^          SIGNPATH_API_TOKEN: \$\{\{ (.+) \}\}$/m)?.[1];
  assert.ok(expression);
  assert.equal(evaluate(expression, 'unsigned', undefined, 'fixture-secret'), '');
  assert.equal(evaluate(expression, 'release-signing', undefined, 'fixture-secret'), 'fixture-secret');
  assert.equal(evaluate(expression, 'test-signing', undefined, 'fixture-secret'), 'fixture-secret');
});
test('credential, allowed-tag and version gates precede repository-script execution', () => {
  const tests = workflow.indexOf('- name: Test release signing policy offline');
  for (const name of ['Validate signing configuration', 'Verify tag points to latest commit of an allowed release branch', 'Verify compiled version fields'])
    assert.ok(workflow.indexOf('- name: ' + name) < tests, name);
});
test('manual default and tag-push signing policy stay unchanged', () => {
  assert.match(workflow, /default: test-signing\n        options:\n          - test-signing\n          - release-signing\n          - unsigned/);
  assert.match(workflow, /SIGNING_POLICY: \$\{\{ github.event_name == 'workflow_dispatch' && inputs.signing_mode \|\| 'release-signing' \}\}/);
  assert.match(workflow, /published: \$\{\{ steps.publish.outputs.published \}\}/);
  assert.match(workflow, /needs.release.outputs.published == 'true'/);
});
test('actual validation allows unsigned without a token', () => {
  const result = powershell(script('Validate signing configuration'));
  assert.equal(result.status, 0, result.output);
});
for (const mode of ['test-signing', 'release-signing']) {
  test(`actual validation still requires SignPath credentials for ${mode}`, () => {
    const result = powershell(script('Validate signing configuration'), { mode });
    assert.notEqual(result.status, 0); assert.match(result.output, /SIGNPATH_API_TOKEN/);
  });
}
for (const mode of ['unsigned', 'release-signing']) {
  test(`actual validation requires an explicit manual tag for ${mode}`, () => {
    const result = powershell(script('Validate signing configuration'), { mode, tag: '', token: mode === 'release-signing' ? 'fixture' : '' });
    assert.notEqual(result.status, 0); assert.match(result.output, /Git tag is required/);
  });
}
test('unknown signing policies fail closed', () => {
  const result = powershell(script('Validate signing configuration'), { mode: 'invalid' });
  assert.notEqual(result.status, 0); assert.match(result.output, /Unsupported signing policy/);
});
test('test signing without a tag still resolves the project version', () => {
  const result = powershell(script('Validate signing configuration'), { mode: 'test-signing', tag: '', token: 'fixture' });
  assert.equal(result.status, 0, result.output);
  const version = readFileSync(join(root, 'WinPieGestures/WinPieGestures.csproj'), 'utf8').match(/<Version>([^<]+)<\/Version>/)[1];
  assert.ok(readFileSync(result.envFile, 'utf8').includes(`RELEASE_TAG=v${version}`));
});
test('test release tags have not been added by this change', () => {
  const result = powershell(script('Verify compiled version fields'), { tag: 'v2.0.1-test.1' });
  assert.notEqual(result.status, 0); assert.match(result.output, /Release tag must use/);
});

function unsignedFixture({ status = 'NotSigned', certificate = false, empty = false, missing = false, duplicate = false, dll = false } = {}) {
  const directory = mkdtempSync(join(scratch, 'exe-'));
  const files = ['Lightweight.exe', 'Standalone.exe', dll ? 'Setup.dll' : 'Setup.exe'].map(name => join(directory, name));
  files.forEach((file, index) => { if (!(missing && index === 2)) writeFileSync(file, empty && index === 2 ? '' : 'metadata fixture only; never executed'); });
  if (duplicate) files[2] = files[0];
  const quote = text => `'${text.replaceAll("'", "''")}'`;
  return powershell(`
$global:UnsignedFixtureReads = 0
function Get-AuthenticodeSignature {
  [CmdletBinding()] param([string]$LiteralPath)
  $global:UnsignedFixtureReads++
  [pscustomobject]@{ Status = $env:FAKE_SIGNATURE_STATUS; SignerCertificate = $(if ($env:FAKE_CERTIFICATE -eq 'true') { 'fixture-certificate' } else { $null }) }
}
& ${quote(join(root, '.github/scripts/Assert-UnsignedRelease.ps1'))} -Path @(${files.map(quote).join(',')})
Write-Output ('FIXTURE_SIGNATURE_READS=' + $global:UnsignedFixtureReads)
`, { env: { FAKE_SIGNATURE_STATUS: status, FAKE_CERTIFICATE: String(certificate) } });
}
test('unsigned verifier accepts three distinct nonempty NotSigned files without reading trust stores', () => {
  const result = unsignedFixture(); assert.equal(result.status, 0, result.output);
  assert.match(result.output, /^FIXTURE_SIGNATURE_READS=3\r?$/m);
});
for (const status of ['Valid', 'NotTrusted', 'HashMismatch', 'UnknownError']) {
  test(`unsigned verifier refuses ${status} status`, () => {
    const result = unsignedFixture({ status, certificate: status !== 'UnknownError' });
    assert.notEqual(result.status, 0); assert.match(result.output, /refuses signed or unverifiable/);
  });
}
test('unsigned verifier refuses even NotSigned if a signer certificate is present', () => {
  const result = unsignedFixture({ certificate: true });
  assert.notEqual(result.status, 0); assert.match(result.output, /refuses signed or unverifiable/);
});
for (const options of [{ empty: true }, { missing: true }, { duplicate: true }, { dll: true }]) {
  test(`unsigned verifier rejects invalid package targets ${JSON.stringify(options)}`, () => {
    assert.notEqual(unsignedFixture(options).status, 0);
  });
}
for (const mode of ['unsigned', 'release-signing']) {
  test(`actual release notes ${mode === 'unsigned' ? 'include' : 'do not add'} the unsigned warning`, () => {
    const directory = mkdtempSync(join(scratch, 'notes-'));
    writeFileSync(join(directory, 'CHANGELOG.md'), '## [v2.0.1-beta.1] - 2026-10-23\n\n- Fixture release notes.\n');
    const result = powershell(script('Extract Release Notes from CHANGELOG.md'), { mode, cwd: directory });
    assert.equal(result.status, 0, result.output);
    const notes = readFileSync(join(directory, 'dist/RELEASE_NOTES.md'), 'utf8');
    assert.ok(notes.includes('Fixture release notes.'));
    assert.equal(notes.includes('未签名构建 / Unsigned build'), mode === 'unsigned');
    if (mode === 'unsigned') assert.ok(notes.includes('SHA256SUMS.txt'));
  });
}
test('actual unsigned workflow step verifies the three intended packaging targets', () => {
  const directory = mkdtempSync(join(scratch, 'workflow-'));
  const paths = ['publish/Lightweight/StarPie.exe', 'publish/Standalone/StarPie.exe', 'dist/StarPie-v2.0.1-beta.1-Setup-win-x64.exe'];
  for (const path of paths) { mkdirSync(dirname(join(directory, path)), { recursive: true }); writeFileSync(join(directory, path), 'fixture only'); }
  mkdirSync(join(directory, '.github/scripts'), { recursive: true });
  copyFileSync(join(root, '.github/scripts/Assert-UnsignedRelease.ps1'), join(directory, '.github/scripts/Assert-UnsignedRelease.ps1'));
  const body = `$global:VerifiedTargets = [System.Collections.Generic.List[string]]::new()
function Get-AuthenticodeSignature {
  [CmdletBinding()] param([string]$LiteralPath)
  $global:VerifiedTargets.Add($LiteralPath)
  [pscustomobject]@{ Status = 'NotSigned'; SignerCertificate = $null }
}
${script('Verify unsigned executables')}
Write-Output ('FIXTURE_TARGETS=' + (ConvertTo-Json -InputObject @($global:VerifiedTargets) -Compress))`;
  const result = powershell(body, { cwd: directory });
  assert.equal(result.status, 0, result.output);
  const targets = JSON.parse(result.output.match(/FIXTURE_TARGETS=(\[[^\r\n]*\])/)[1]);
  assert.deepEqual(targets.map(path => relative(directory, path).replaceAll('\\', '/')), paths);
});
test('unsigned warning is present even when the changelog is absent', () => {
  const directory = mkdtempSync(join(scratch, 'missing-notes-'));
  const result = powershell(script('Extract Release Notes from CHANGELOG.md'), { cwd: directory });
  assert.equal(result.status, 0, result.output);
  assert.ok(readFileSync(join(directory, 'dist/RELEASE_NOTES.md'), 'utf8').includes('未签名构建 / Unsigned build'));
});
test('unsigned verification is required before packaging and publication', () => {
  assert.ok(workflow.indexOf('- name: Verify unsigned executables') < workflow.indexOf('- name: Package release assets'));
  assert.ok(workflow.indexOf('- name: Verify unsigned executables') < workflow.indexOf('- name: Create GitHub Release'));
  assert.doesNotMatch(step('Verify unsigned executables'), /continue-on-error/);
  assert.equal((script('Verify unsigned executables').match(/StarPie\.exe|Setup-win-x64\.exe/g) || []).length, 3);
});
