'use strict';

const vscode = require('vscode');
const path = require('node:path');
const { LanguageClient } = require('vscode-languageclient/node');

let client;
let watcher;
let restarting = Promise.resolve();
let extensionPath;

function compiler() {
    const config = vscode.workspace.getConfiguration('rukbat.server');
    const command = config.get('command', '');
    if (command) return { command, args: config.get('arguments', []) };
    if (process.platform !== 'win32') throw new Error('The bundled compiler requires Windows. Configure an external compiler for this host.');
    return { command: path.join(extensionPath, 'runtime', 'win-x64', 'rukbat.exe'), args: [] };
}

async function execute(action) {
    if (!vscode.workspace.isTrusted) {
        void vscode.window.showErrorMessage('Trust this workspace before running Rukbat commands.');
        return;
    }
    const document = vscode.window.activeTextEditor?.document;
    if (action !== 'doctor' && (!document || document.languageId !== 'rukbat' || document.uri.scheme !== 'file')) {
        void vscode.window.showErrorMessage('Open a saved .rukbat file before running this command.');
        return;
    }
    if (!await vscode.workspace.saveAll(false)) return;
    const folder = document && vscode.workspace.getWorkspaceFolder(document.uri);
    const cwd = folder?.uri.fsPath || (document ? path.dirname(document.uri.fsPath) : extensionPath);
    const target = action === 'test' ? cwd : document?.uri.fsPath;
    let executable;
    try { executable = compiler(); }
    catch (error) { void vscode.window.showErrorMessage(error.message); return; }
    const args = [...executable.args, action];
    if (action !== 'doctor') args.push(target);
    if (action === 'run') {
        const supplied = await vscode.window.showInputBox({
            title: 'Rukbat: Run', prompt: 'Program arguments as a JSON string array. Each item is one argument.', value: '[]',
            validateInput(value) {
                try { const items = JSON.parse(value); if (Array.isArray(items) && items.every(item => typeof item === 'string')) return undefined; }
                catch { /* Validation is displayed in the input box. */ }
                return 'Enter a JSON array of strings, for example ["config.json", "report.json"].';
            }
        });
        if (supplied === undefined) return;
        args.push('--', ...JSON.parse(supplied));
    }
    const task = new vscode.Task({ type: 'rukbat', action }, folder || vscode.TaskScope.Global,
        `Rukbat: ${action}`, 'Rukbat', new vscode.ProcessExecution(executable.command, args, { cwd }), ['$rukbat']);
    task.presentationOptions = { reveal: vscode.TaskRevealKind.Always, panel: vscode.TaskPanelKind.Dedicated, clear: true };
    return vscode.tasks.executeTask(task);
}

async function restart() {
    if (client) {
        const previous = client;
        client = undefined;
        await previous.dispose();
    }
    if (!vscode.workspace.isTrusted) return;
    let executable;
    try { executable = compiler(); }
    catch (error) { void vscode.window.showErrorMessage(error.message); return; }
    const command = executable.command;
    const args = [...executable.args, 'lsp'];
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
            `Could not start Rukbat: ${error.message}. Reinstall the complete Windows VSIX, or check your external compiler settings.`, 'Open Settings')
            .then(selected => { if (selected) return vscode.commands.executeCommand('workbench.action.openSettings', 'rukbat.server'); });
    }
}

function queueRestart() {
    restarting = restarting.then(restart, restart);
    return restarting;
}

async function activate(context) {
    extensionPath = context.extensionPath;
    watcher = vscode.workspace.createFileSystemWatcher('**/*.rukbat');
    context.subscriptions.push(
        watcher,
        vscode.commands.registerCommand('rukbat.restartServer', queueRestart),
        vscode.workspace.onDidGrantWorkspaceTrust(queueRestart),
        vscode.workspace.onDidChangeConfiguration(event => {
            if (event.affectsConfiguration('rukbat.server')) void queueRestart();
        })
    );
    for (const action of ['check', 'test', 'build', 'run', 'doctor']) {
        context.subscriptions.push(vscode.commands.registerCommand(`rukbat.${action}`, () => execute(action)));
    }
    await queueRestart();
}

async function deactivate() {
    await restarting;
    if (client) await client.dispose();
    client = undefined;
    watcher = undefined;
}

module.exports = { activate, deactivate };
