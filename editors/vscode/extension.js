'use strict';

const vscode = require('vscode');
const { LanguageClient } = require('vscode-languageclient/node');

let client;
let watcher;
let restarting = Promise.resolve();

async function restart() {
    if (client) {
        const previous = client;
        client = undefined;
        await previous.dispose();
    }
    if (!vscode.workspace.isTrusted) return;
    const config = vscode.workspace.getConfiguration('rukbat.server');
    const command = config.get('command', 'rukbat');
    const args = [...config.get('arguments', []), 'lsp'];
    client = new LanguageClient('rukbat', 'Rukbat', { command, args }, {
        documentSelector: [{ scheme: 'file', language: 'rukbat' }],
        synchronize: { fileEvents: watcher }
    });
    try {
        await client.start();
    } catch (error) {
        await client.dispose();
        client = undefined;
        void vscode.window.showErrorMessage(
            `Could not start Rukbat: ${error.message}. Configure rukbat.server.command and rukbat.server.arguments.`, 'Open Settings')
            .then(selected => { if (selected) return vscode.commands.executeCommand('workbench.action.openSettings', 'rukbat.server'); });
    }
}

function queueRestart() {
    restarting = restarting.then(restart, restart);
    return restarting;
}

async function activate(context) {
    watcher = vscode.workspace.createFileSystemWatcher('**/*.rukbat');
    context.subscriptions.push(
        watcher,
        vscode.commands.registerCommand('rukbat.restartServer', queueRestart),
        vscode.workspace.onDidGrantWorkspaceTrust(queueRestart),
        vscode.workspace.onDidChangeConfiguration(event => {
            if (event.affectsConfiguration('rukbat.server')) void queueRestart();
        })
    );
    await queueRestart();
}

async function deactivate() {
    await restarting;
    if (client) await client.dispose();
    client = undefined;
    watcher = undefined;
}

module.exports = { activate, deactivate };
