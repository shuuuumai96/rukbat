'use strict';

// Maintainer acceptance for a previously extracted CLI ZIP and VSIX, without PATH .NET.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const directory = path.resolve(process.argv[2]);
const executable = path.join(directory, 'rukbat.exe');
const work = fs.mkdtempSync(path.join(directory, 'acceptance-'));
const env = { ...process.env, PATH: `${process.env.SystemRoot}/System32;${process.env.SystemRoot}/System32/WindowsPowerShell/v1.0`,
    DOTNET_ROOT: path.join(work, 'no-dotnet'), DOTNET_ROOT_X64: path.join(work, 'no-dotnet'), DOTNET_MULTILEVEL_LOOKUP: '0' };
const config = JSON.parse(fs.readFileSync(path.join(directory, 'rukbat.runtimeconfig.json'), 'utf8'));
assert.ok(config.runtimeOptions.includedFrameworks?.length);
assert.equal(config.runtimeOptions.framework, undefined);
assert.equal(config.runtimeOptions.frameworks, undefined);
function run(args, expected = 0) {
    const result = spawnSync(executable, args, { cwd: work, env, encoding: 'utf8', timeout: 60000, windowsHide: true });
    if (result.error) throw result.error;
    assert.equal(result.status, expected, result.stdout + '\n' + result.stderr);
    return result.stdout;
}
const manifest = JSON.parse(fs.readFileSync(path.join(directory, 'distribution.json'), 'utf8'));
assert.equal(run(['--version']).trim(), `rukbat ${manifest.version}`);
assert.match(run(['doctor']), /OK: Local PowerShell/);
const source = path.join(work, 'main.rukbat');
fs.writeFileSync(source, 'import encoding\nimport process\nimport http\nprint("standalone")\nprint(http.status_200_ok)\nfor argument in args() { print(argument) }\n');
run(['check', source]);
run(['fmt', source]);
const documentationSource = path.join(work, 'documentation.rukbat');
fs.writeFileSync(documentationSource, '//! Offline documentation.\n//! ```rukbat check\n//! const value := 1\n//! print(value)\n//! ```\n/// A helper.\nfn helper(): void {}\n/// Retry limit.\nconst limit := 3\n');
run(['doc', documentationSource, '--check']);
run(['doc', documentationSource]);
assert.match(fs.readFileSync(path.join(work, 'documentation.html'), 'utf8'), /Statically checked; not executed/);
assert.match(fs.readFileSync(path.join(work, 'documentation.html'), 'utf8'), /const limit: int/);
fs.unlinkSync(documentationSource);
run(['build', source]);
const args = ['a b', '日本', '$(literal)', '"quoted"'];
const output = run(['run', source, '--', ...args]);
assert.match(output, /standalone/);
for (const arg of args) assert.ok(output.includes(arg), output);
fs.writeFileSync(path.join(work, 'test_main.rukbat'), 'import test\nfn test_ok(): void { test.assert(2 + 2 == 4) }\n');
assert.match(run(['test', work]), /PASS/);
const requests = [
    { jsonrpc: '2.0', id: 1, method: 'initialize', params: { capabilities: {} } },
    { jsonrpc: '2.0', id: 2, method: 'shutdown', params: null },
    { jsonrpc: '2.0', method: 'exit' }
].map(value => { const json = JSON.stringify(value); return `Content-Length: ${Buffer.byteLength(json)}\r\n\r\n${json}`; }).join('');
const lsp = spawnSync(executable, ['lsp'], { cwd: work, env, input: requests, encoding: 'utf8', timeout: 15000, windowsHide: true });
assert.equal(lsp.status, 0, lsp.stderr);
assert.match(lsp.stdout, /completionProvider/);
fs.unlinkSync(source);
fs.unlinkSync(path.join(work, 'test_main.rukbat'));
for (const shell of [path.join(process.env.SystemRoot, 'System32/WindowsPowerShell/v1.0/powershell.exe'),
    path.join(process.env.ProgramFiles, 'PowerShell/7/pwsh.exe')]) {
    if (!fs.existsSync(shell)) continue;
    const result = spawnSync(shell, ['-NoLogo', '-NoProfile', '-File', path.join(work, 'main.ps1'), ...args],
        { cwd: work, env, encoding: 'utf8', timeout: 30000, windowsHide: true });
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /standalone/);
    console.log(`Passed source-free script: ${shell}`);
}
console.log(`Passed self-contained --version/doctor/check/fmt/doc/build/run/test/LSP: ${directory}`);
