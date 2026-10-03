'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');

function harness(trusted = true, failStart = false) {
    const instances = [];
    const handlers = {};
    let watchers = 0;
    let failures = 0;
    const watcher = { dispose() {} };
    class Client {
        constructor(id, name, server, options) {
            Object.assign(this, { server, options, disposed: false });
            instances.push(this);
        }
        async start() { if (failStart) throw new Error('Compiler is unavailable'); }
        async dispose() { this.disposed = true; }
    }
    const disposable = () => ({ dispose() {} });
    const vscode = {
        workspace: {
            isTrusted: trusted,
            createFileSystemWatcher() { watchers++; return watcher; },
            getConfiguration() { return { get: key => key === 'command' ? 'dotnet' : ['C:/compiler/rukbat.dll'] }; },
            onDidGrantWorkspaceTrust(handler) { handlers.trust = handler; return disposable(); },
            onDidChangeConfiguration(handler) { handlers.configuration = handler; return disposable(); }
        },
        commands: {
            registerCommand(name, handler) { handlers.restart = handler; return disposable(); },
            async executeCommand() {}
        },
        window: { showErrorMessage() { failures++; return new Promise(() => {}); } }
    };
    const context = { subscriptions: [] };
    const module = { exports: {} };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../extension.js'), 'utf8'), {
        module, require: name => name === 'vscode' ? vscode : { LanguageClient: Client }
    });
    return { extension: module.exports, context, vscode, handlers, instances, watcher, watchers: () => watchers, failures: () => failures, allowStart: () => { failStart = false; } };
}

test('untrusted workspaces never launch a compiler until trust is granted', async () => {
    const h = harness(false);
    await h.extension.activate(h.context);
    assert.equal(h.instances.length, 0);
    h.vscode.workspace.isTrusted = true;
    await h.handlers.trust();
    assert.equal(h.instances.length, 1);
    assert.equal(h.instances[0].server.command, 'dotnet');
    assert.equal(h.instances[0].server.args.join('|'), 'C:/compiler/rukbat.dll|lsp');
    await h.extension.deactivate();
    assert.ok(h.instances[0].disposed);
});

test('restart disposes the previous client and reuses the one managed watcher', async () => {
    const h = harness();
    await h.extension.activate(h.context);
    await h.handlers.restart();
    assert.equal(h.instances.length, 2);
    assert.ok(h.instances[0].disposed);
    assert.equal(h.watchers(), 1);
    assert.equal(h.instances[1].options.synchronize.fileEvents, h.watcher);
    assert.ok(h.context.subscriptions.includes(h.watcher));
    await h.extension.deactivate();
    assert.ok(h.instances[1].disposed);
});

test('a missing compiler reports the configuration problem without blocking restart', async () => {
    const h = harness(true, true);
    await h.extension.activate(h.context);
    assert.equal(h.failures(), 1);
    assert.ok(h.instances[0].disposed);
    h.allowStart();
    await h.handlers.restart();
    assert.equal(h.instances.length, 2);
    await h.extension.deactivate();
    assert.ok(h.instances[1].disposed);
});
