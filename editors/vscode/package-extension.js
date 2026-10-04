'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const runtime = path.join(__dirname, 'runtime/win-x64');
for (const file of ['rukbat.exe', 'rukbat.dll', 'rukbat.runtimeconfig.json', 'coreclr.dll', 'System.Management.Automation.dll']) {
    if (!fs.existsSync(path.join(runtime, file))) throw new Error(`Missing bundled runtime ${file}. Run tools/package.ps1 first.`);
}
const manifest = require(path.join(runtime, 'distribution.json'));
const version = require('./package.json').version;
if (manifest.version !== version || manifest.runtime !== 'win-x64') throw new Error('Extension and compiler distribution versions do not match.');
fs.mkdirSync(path.join(__dirname, 'artifacts'), { recursive: true });
const result = spawnSync(process.execPath, [require.resolve('@vscode/vsce/vsce'), 'package', '--target', 'win32-x64',
    '--allow-missing-repository', '--skip-license', '--out', `artifacts/rukbat-${version}-win32-x64.vsix`], { cwd: __dirname, stdio: 'inherit' });
if (result.error) throw result.error;
process.exitCode = result.status ?? 1;
