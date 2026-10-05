'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { test } = require('node:test');

function harness(trusted = true, failStart = false, bundled = false) {
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
            getConfiguration() { return { get: key => key === 'command' ? (bundled ? '' : 'dotnet') : ['C:/compiler/rukbat.dll'] }; },
            async saveAll() { return true; },
            getWorkspaceFolder() { return { uri: { fsPath: 'C:/work space' } }; },
            onDidGrantWorkspaceTrust(handler) { handlers.trust = handler; return disposable(); },
            onDidChangeConfiguration(handler) { handlers.configuration = handler; return disposable(); }
        },
        commands: {
            registerCommand(name, handler) { handlers[name] = handler; if (name === 'rukbat.restartServer') handlers.restart = handler; return disposable(); },
            async executeCommand() {}
        },
        window: { showErrorMessage() { failures++; return new Promise(() => {}); },
            activeTextEditor: { document: { languageId: 'rukbat', uri: { scheme: 'file', fsPath: 'C:/work space/main.rukbat' } } },
            async showInputBox() { return '["a b", "$(danger)", "\\\"quote\\\""]'; } },
        Task: class { constructor(definition, scope, name, source, execution, matchers) { Object.assign(this, { definition, scope, name, source, execution, matchers }); } },
        ProcessExecution: class { constructor(command, args, options) { Object.assign(this, { command, args, options }); } },
        TaskScope: { Global: 1 }, TaskRevealKind: { Always: 1 }, TaskPanelKind: { Dedicated: 1 },
        tasks: { async executeTask(task) { return task; } }
    };
    vscode.debug = {
        registerDebugConfigurationProvider(type, provider) { handlers.debugProvider = provider; return disposable(); },
        registerDebugAdapterDescriptorFactory(type, factory) { handlers.debugFactory = factory; return disposable(); },
        async startDebugging(folder, config) { return handlers.debugProvider.resolveDebugConfiguration(folder, config); }
    };
    vscode.DebugAdapterInlineImplementation = class { constructor(adapter) { this.adapter = adapter; } };
    const context = { subscriptions: [], extensionPath: 'C:/extension' };
    const module = { exports: {} };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../extension.js'), 'utf8'), {
        module, process: { platform: 'win32' }, require: name => name === 'vscode' ? vscode : name === 'node:path' ? path :
            name === './debug-adapter' ? require('../debug-adapter') : { LanguageClient: Client }
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

test('default compiler is bundled and never resolved from PATH', async () => {
    const h = harness(true, false, true);
    await h.extension.activate(h.context);
    assert.equal(h.instances[0].server.command, path.join('C:/extension', 'runtime', 'win-x64', 'rukbat.exe'));
    assert.equal(h.instances[0].server.args.join('|'), 'lsp');
    const task = await h.handlers['rukbat.build']();
    assert.equal(task.execution.command, h.instances[0].server.command);
    assert.equal(task.execution.args.join('|'), 'build|C:/work space/main.rukbat');
    await h.extension.deactivate();
});

test('actions preserve literal arguments and use the same compiler as LSP', async () => {
    const h = harness();
    await h.extension.activate(h.context);
    const task = await h.handlers['rukbat.run']();
    assert.equal(task.execution.command, 'dotnet');
    assert.equal(task.execution.args.join('|'), 'C:/compiler/rukbat.dll|run|C:/work space/main.rukbat|--|a b|$(danger)|"quote"');
    assert.equal(task.execution.options.cwd, 'C:/work space');
    const tests = await h.handlers['rukbat.test']();
    const docs = await h.handlers['rukbat.doc']();
    assert.equal(docs.execution.command, 'dotnet');
    assert.equal(docs.execution.args.join('|'), 'C:/compiler/rukbat.dll|doc|C:/work space/main.rukbat');
    assert.equal(tests.execution.args.join('|'), 'C:/compiler/rukbat.dll|test|C:/work space');
    await h.extension.deactivate();
});

test('untrusted, unsaved and canceled-save actions do not start tasks', async () => {
    const h = harness(false);
    await h.extension.activate(h.context);
    assert.equal(await h.handlers['rukbat.run'](), undefined);
    h.vscode.workspace.isTrusted = true;
    h.vscode.workspace.saveAll = async () => false;
    assert.equal(await h.handlers['rukbat.build'](), undefined);
    h.vscode.window.activeTextEditor.document.uri.scheme = 'untitled';
    assert.equal(await h.handlers['rukbat.test'](), undefined);
    assert.equal(h.failures(), 2);
    await h.extension.deactivate();
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

test('debug launch uses the active source, saves files, and enforces workspace trust', async () => {
    const h = harness();
    await h.extension.activate(h.context);
    const config = await h.handlers['rukbat.debug']();
    assert.equal(config.program, 'C:/work space/main.rukbat');
    assert.equal(config.type, 'rukbat');
    assert.equal(config.stopOnEntry, true);
    const descriptor = h.handlers.debugFactory.createDebugAdapterDescriptor();
    assert.equal(descriptor.adapter.compiler.command, 'dotnet');
    descriptor.adapter.dispose();
    h.vscode.workspace.saveAll = async () => false;
    assert.equal(await h.handlers['rukbat.debug'](), undefined);
    h.vscode.workspace.isTrusted = false;
    assert.equal(await h.handlers['rukbat.debug'](), undefined);
    assert.throws(() => h.handlers.debugFactory.createDebugAdapterDescriptor(), /Trust/);
    await h.extension.deactivate();
});
