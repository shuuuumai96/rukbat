'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { runTests } = require('@vscode/test-electron');

async function main() {
    const root = path.resolve(__dirname, '..');
    const dll = process.env.RUKBAT_TEST_DLL || path.resolve(root, '../../src/Rukbat/bin/Debug/net8.0/rukbat.dll');
    if (!fs.existsSync(dll)) throw new Error(`Build the compiler before testing: ${dll}`);
    const workspace = path.join(root, '.vscode-test/workspace');
    fs.mkdirSync(path.join(workspace, '.vscode'), { recursive: true });
    fs.writeFileSync(path.join(workspace, '.vscode/settings.json'), JSON.stringify({
        'rukbat.server.command': 'dotnet', 'rukbat.server.arguments': [dll], 'rukbat.trace.server': 'verbose'
    }));
    await runTests({
        extensionDevelopmentPath: root,
        extensionTestsPath: path.join(__dirname, 'editor-suite.js'),
        launchArgs: [workspace, '--disable-extensions', '--skip-welcome', '--skip-release-notes', '--disable-workspace-trust',
            '--user-data-dir=' + path.join(root, '.vscode-test/user-data')]
    });
}

main().catch(error => { console.error(error); process.exitCode = 1; });
