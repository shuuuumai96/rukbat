'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vscode = require('vscode');

async function until(check, label) {
    const deadline = Date.now() + 15000;
    while (Date.now() < deadline) {
        const result = await check();
        if (result) return result;
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    throw new Error(`Timed out: ${label}`);
}

async function run() {
    const root = vscode.workspace.workspaceFolders[0].uri.fsPath;
    fs.writeFileSync(path.join(root, 'helper.rukbat'), '/// インポートした関数の説明。\nfn number(): int { return 1 }\n');
    fs.writeFileSync(path.join(root, 'main.rukbat'), 'import "./helper.rukbat" as helper\nvalue: int = helper.number()\nprint(missing)\n');
    const uri = vscode.Uri.file(path.join(root, 'main.rukbat'));
    const document = await vscode.workspace.openTextDocument(uri);
    await vscode.window.showTextDocument(document);
    assert.equal(document.languageId, 'rukbat');
    await vscode.extensions.getExtension('rukbat.rukbat').activate();
    await until(() => vscode.languages.getDiagnostics(uri).some(d => d.code === 'RUK2004'), 'compiler diagnostics');
    const edit = new vscode.WorkspaceEdit();
    edit.replace(uri, new vscode.Range(2, 6, 2, 13), 'value');
    assert.ok(await vscode.workspace.applyEdit(edit));
    try { await until(() => vscode.languages.getDiagnostics(uri).length === 0, 'unsaved diagnostic repair'); }
    catch (error) {
        console.error('Document after edit:', document.getText());
        console.error('Diagnostics after edit:', vscode.languages.getDiagnostics(uri));
        throw error;
    }

    const hovers = await until(async () => {
        const result = await vscode.commands.executeCommand('vscode.executeHoverProvider', uri, new vscode.Position(2, 7));
        return result && result.length ? result : null;
    }, 'hover');
    assert.ok(hovers[0].contents.some(c => (c.value || c).includes('value: int')));
    const importedHover = await vscode.commands.executeCommand('vscode.executeHoverProvider', uri, new vscode.Position(1, 23));
    assert.ok(importedHover.some(h => h.contents.some(c => (c.value || c).includes('インポートした関数の説明。'))));
    const definitions = await vscode.commands.executeCommand('vscode.executeDefinitionProvider', uri, new vscode.Position(1, 23));
    assert.equal((definitions[0].uri || definitions[0].targetUri).fsPath, path.join(root, 'helper.rukbat'));

    // LSP references and rename use the unsaved buffer and standard editor Undo.
    const references = await vscode.commands.executeCommand('vscode.executeReferenceProvider', uri, new vscode.Position(2, 7));
    assert.equal(references.filter(location => location.uri.fsPath === uri.fsPath).length, 2);
    const rename = await vscode.commands.executeCommand('vscode.executeDocumentRenameProvider', uri, new vscode.Position(1, 2), 'count');
    assert.ok(rename && rename.size > 0, 'The client must negotiate versioned transactional rename.');
    assert.ok(await vscode.workspace.applyEdit(rename));
    assert.equal(document.getText().split('\n')[1], 'count: int = helper.number()');
    assert.equal(document.getText().split('\n')[2], 'print(count)');
    assert.ok(fs.readFileSync(uri.fsPath, 'utf8').includes('value: int'));
    await vscode.commands.executeCommand('undo');
    assert.equal(document.getText().split('\n')[1], 'value: int = helper.number()');
    assert.equal(document.getText().split('\n')[2], 'print(value)');

    const signatureUri = vscode.Uri.file(path.join(root, 'signature.rukbat'));
    fs.writeFileSync(signatureUri.fsPath, 'fn add(a: int, b: int): int { return a + b }\nprint(add(1, \n');
    const signatureDocument = await vscode.workspace.openTextDocument(signatureUri);
    await vscode.window.showTextDocument(signatureDocument);
    const signature = await until(async () => {
        const result = await vscode.commands.executeCommand('vscode.executeSignatureHelpProvider', signatureUri, new vscode.Position(1, 13), ',');
        return result?.signatures?.length ? result : null;
    }, 'incomplete call signature help');
    assert.equal(signature.signatures[0].label, 'fn add(a: int, b: int): int');
    assert.equal(signature.activeParameter, 1);
    // Leave the fixture valid so later project commands can check every source.
    const finishSignature = new vscode.WorkspaceEdit();
    finishSignature.replace(signatureUri, new vscode.Range(1, 0, 2, 0), 'print(add(1, 2))\n');
    assert.ok(await vscode.workspace.applyEdit(finishSignature));
    const cliUri = vscode.Uri.file(path.join(root, 'cli-options.rukbat'));
    const cliSource = 'import cli\ncommand := cli.command("demo.ps1", "Demo")\noption := cli.option("pattern", "Pattern")\noption.default_value = "*"\ncommand.options = [option]\n';
    fs.writeFileSync(cliUri.fsPath, cliSource);
    const cliDocument = await vscode.workspace.openTextDocument(cliUri);
    await vscode.window.showTextDocument(cliDocument);
    await until(async () => {
        const completion = await vscode.commands.executeCommand('vscode.executeCompletionItemProvider', cliUri, new vscode.Position(2, 14));
        return completion?.items?.some(item => item.label === 'parse');
    }, 'cli module completion');
    await until(async () => {
        const hover = await vscode.commands.executeCommand('vscode.executeHoverProvider', cliUri, new vscode.Position(2, 17));
        return hover?.some(item => item.contents.some(content => (content.value || content).includes('cli.option(name: string, description: string): cli.Option')));
    }, 'cli option hover');
    const unfinishedCli = new vscode.WorkspaceEdit();
    unfinishedCli.replace(cliUri, new vscode.Range(2, 0, 3, 0), 'option := cli.option("pattern", \n');
    assert.ok(await vscode.workspace.applyEdit(unfinishedCli));
    await until(async () => {
        const help = await vscode.commands.executeCommand('vscode.executeSignatureHelpProvider', cliUri, new vscode.Position(2, 31), ',');
        return help?.signatures?.[0]?.label === 'cli.option(name: string, description: string): cli.Option' && help.activeParameter === 1;
    }, 'cli incomplete signature help');
    const restoreCli = new vscode.WorkspaceEdit();
    restoreCli.replace(cliUri, new vscode.Range(0, 0, cliDocument.lineCount, 0), cliSource);
    assert.ok(await vscode.workspace.applyEdit(restoreCli));
    await vscode.window.showTextDocument(document);

    const docUri = vscode.Uri.file(path.join(root, 'hover.rukbat'));
    fs.writeFileSync(docUri.fsPath, '/// 二つの整数を加算します。\n///\n/// **注意:** 整数の範囲を超えるとErrorになります。\nfn add(a: int, b: int): int { return a + b }\nprint(add(1, 2))\n');
    const docDocument = await vscode.workspace.openTextDocument(docUri);
    await vscode.window.showTextDocument(docDocument);
    async function functionHover(line, character, expected) {
        return until(async () => {
            const result = await vscode.commands.executeCommand('vscode.executeHoverProvider', docUri, new vscode.Position(line, character));
            return result?.some(h => h.contents.some(c => (c.value || c).includes(expected))) ? result : null;
        }, `function documentation at ${line}:${character}`);
    }
    for (const position of [[3, 4], [4, 7]]) {
        const result = await functionHover(...position, '二つの整数を加算します。');
        assert.ok(result.some(h => h.contents.some(c => (c.value || c).includes('**注意:**'))));
    }
    const docEdit = new vscode.WorkspaceEdit();
    docEdit.replace(docUri, new vscode.Range(0, 4, 0, 16), '未保存の説明も反映します。');
    assert.ok(await vscode.workspace.applyEdit(docEdit));
    await functionHover(4, 7, '未保存の説明も反映します。');
    assert.ok(fs.readFileSync(docUri.fsPath, 'utf8').includes('二つの整数を加算します。'));

    const constUri = vscode.Uri.file(path.join(root, 'const.rukbat'));
    fs.writeFileSync(constUri.fsPath, '/// 再試行回数。\nconst retries := 3\nprint(retries)\nretries = 4\n');
    const constDocument = await vscode.workspace.openTextDocument(constUri);
    await vscode.window.showTextDocument(constDocument);
    await until(() => vscode.languages.getDiagnostics(constUri).some(d => d.code === 'RUK2011'), 'const write diagnostic');
    for (const position of [[1, 7], [2, 8]]) {
        const constHover = await vscode.commands.executeCommand('vscode.executeHoverProvider', constUri, new vscode.Position(...position));
        assert.ok(constHover.some(h => h.contents.some(c => (c.value || c).includes('const retries: int'))));
    }
    const repairConst = new vscode.WorkspaceEdit();
    repairConst.delete(constUri, new vscode.Range(3, 0, 4, 0));
    assert.ok(await vscode.workspace.applyEdit(repairConst));
    await until(() => vscode.languages.getDiagnostics(constUri).length === 0, 'const write repair');

    fs.writeFileSync(path.join(root, 'helper.rukbat'), 'fn number(): string { return "changed" }\n');
    await until(() => vscode.languages.getDiagnostics(uri).some(d => d.code === 'RUK2001'), 'watched imported-file change');
    fs.writeFileSync(path.join(root, 'helper.rukbat'), 'fn number(): int { return 1 }\n');
    await until(() => vscode.languages.getDiagnostics(uri).length === 0, 'watched imported-file repair');

    fs.writeFileSync(path.join(root, 'completion.rukbat'), 'import fs\nvalue := fs.\n');
    const completionUri = vscode.Uri.file(path.join(root, 'completion.rukbat'));
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(completionUri));
    const completions = await until(async () => {
        const result = await vscode.commands.executeCommand('vscode.executeCompletionItemProvider', completionUri, new vscode.Position(1, 12), '.');
        return result && result.items.some(i => i.label === 'read_text') ? result : null;
    }, 'standard-library completion');
    assert.ok(completions.items.some(i => i.label === 'write_text'));

    fs.writeFileSync(path.join(root, 'format.rukbat'), 'x:=1\n\n');
    const formatUri = vscode.Uri.file(path.join(root, 'format.rukbat'));
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(formatUri));
    const formatting = await until(async () => {
        const result = await vscode.commands.executeCommand('vscode.executeFormatDocumentProvider', formatUri, { tabSize: 8, insertSpaces: false });
        return result && result.length ? result : null;
    }, 'canonical formatter');
    // VS Code may minimize the server's whole-document edit into several small edits.
    const formatEdit = new vscode.WorkspaceEdit();
    formatEdit.set(formatUri, formatting);
    assert.ok(await vscode.workspace.applyEdit(formatEdit));
    assert.equal((await vscode.workspace.openTextDocument(formatUri)).getText(), 'x := 1\n');
    assert.equal(fs.readFileSync(formatUri.fsPath, 'utf8'), 'x:=1\n\n');

    await vscode.commands.executeCommand('rukbat.restartServer');
    await until(async () => {
        const result = await vscode.commands.executeCommand('vscode.executeHoverProvider', uri, new vscode.Position(2, 7));
        return result && result.length;
    }, 'restart with unsaved documents');
    assert.equal(document.getText().split('\n')[2], 'print(value)');

    // Execute the actual editor tasks, including the bundled compiler and ProcessExecution.
    const commandUri = vscode.Uri.file(path.join(root, 'task.rukbat'));
    fs.writeFileSync(commandUri.fsPath, '//! Editor task documentation.\n//! ```rukbat check\n//! const example := "example"\n//! print(example)\n//! ```\n/// Task output.\nconst message := "task"\nprint(message)\n');
    fs.writeFileSync(path.join(root, 'test_task.rukbat'), 'import test\nfn test_task(): void { test.assert(true) }\n');
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(commandUri));
    for (const action of ['check', 'build', 'test', 'doc', 'doctor']) {
        const finished = new Promise((resolve, reject) => {
            const timer = setTimeout(() => { listener.dispose(); reject(new Error(`Task timed out: ${action}`)); }, 30000);
            const listener = vscode.tasks.onDidEndTaskProcess(event => {
                if (event.execution.task.definition.action !== action) return;
                clearTimeout(timer); listener.dispose(); resolve(event.exitCode);
            });
        });
        await vscode.commands.executeCommand(`rukbat.${action}`);
        assert.equal(await finished, 0, `Editor task ${action}`);
    }
    assert.ok(fs.existsSync(path.join(root, 'task.ps1')));
    assert.ok(fs.readFileSync(path.join(root, 'task.html'), 'utf8').includes('Editor task documentation.'));
    assert.ok(fs.readFileSync(path.join(root, 'task.html'), 'utf8').includes('const message: string'));

    // Exercise the real inline debug adapter through VS Code's Debug API.
    const debugUri = vscode.Uri.file(path.join(root, 'debug.rukbat'));
    fs.writeFileSync(debugUri.fsPath, 'value := 1\nfn number(n: int): int {\n    result := n + 1\n    return result\n}\nconst result := number(value)\nprint(result)\n');
    const debugDocument = await vscode.workspace.openTextDocument(debugUri);
    await vscode.window.showTextDocument(debugDocument);
    const unsavedDebug = new vscode.WorkspaceEdit();
    unsavedDebug.replace(debugUri, new vscode.Range(0, 9, 0, 10), '2');
    assert.ok(await vscode.workspace.applyEdit(unsavedDebug));
    const breakpoint = new vscode.SourceBreakpoint(new vscode.Location(debugUri, new vscode.Position(5, 0)));
    vscode.debug.addBreakpoints([breakpoint]);
    const debugMessages = [];
    const tracker = vscode.debug.registerDebugAdapterTrackerFactory('rukbat', {
        createDebugAdapterTracker() { return { onDidSendMessage(message) { debugMessages.push(message); } }; }
    });
    try {
        assert.ok(await vscode.debug.startDebugging(vscode.workspace.workspaceFolders[0], {
            type: 'rukbat', request: 'launch', name: 'Editor debug acceptance', program: debugUri.fsPath, stopOnEntry: false
        }));
        await until(() => debugMessages.some(message => message.event === 'stopped'), 'Rukbat breakpoint in VS Code');
        const verifiedBreakpoint = debugMessages.find(message => message.type === 'response' && message.command === 'setBreakpoints')
            .body.breakpoints.find(value => value.line === 6);
        assert.equal(verifiedBreakpoint.verified, true);
        assert.equal(Object.hasOwn(verifiedBreakpoint, 'column'), false, 'A const line breakpoint must not become an inline breakpoint.');
        const session = vscode.debug.activeDebugSession;
        assert.equal(session.type, 'rukbat');
        const stack = await session.customRequest('stackTrace', { threadId: 1 });
        assert.equal(stack.stackFrames[0].source.path, debugUri.fsPath);
        assert.equal(stack.stackFrames[0].line, 6);
        assert.equal((await session.customRequest('evaluate', { expression: 'value', frameId: stack.stackFrames[0].id })).result, '2');
        assert.ok(fs.readFileSync(debugUri.fsPath, 'utf8').startsWith('value := 2'));
        await session.customRequest('next', { threadId: 1 });
        await until(() => debugMessages.filter(message => message.event === 'stopped').length === 2, 'Rukbat step over in VS Code');
        const stepped = await session.customRequest('stackTrace', { threadId: 1 });
        assert.equal(stepped.stackFrames[0].line, 7);
        assert.equal((await session.customRequest('evaluate', { expression: 'result', frameId: stepped.stackFrames[0].id })).result, '3');
        await session.customRequest('continue', { threadId: 1 });
        await until(() => debugMessages.some(message => message.event === 'terminated'), 'Rukbat debug completion in VS Code');
        assert.ok(debugMessages.some(message => message.event === 'output' && message.body.output.includes('3')));
        assert.ok(!fs.readdirSync(root).some(file => file.startsWith('.rukbat-debug-')));
    } finally {
        vscode.debug.removeBreakpoints([breakpoint]);
        tracker.dispose();
        if (vscode.debug.activeDebugSession?.type === 'rukbat') await vscode.debug.stopDebugging();
    }

    // Inspect the actual Variables/Watch responses while stepping with the
    // editor command, including native Int32 fields and copied composite values.
    const inspectionUri = vscode.Uri.file(path.join(root, 'inspection.rukbat'));
    const processCall = code => `process.run("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "[Environment]::Exit(${code})"], options)`;
    fs.writeFileSync(inspectionUri.fsPath, [
        'import encoding',
        'import process',
        'options := process.options(5000, encoding.utf8)',
        'result := ' + processCall(7),
        'fraction := 1.0000000000000002',
        'copies := [result]',
        'print(result.exit_code)',
        'result = ' + processCall(9),
        'print(result.exit_code)'
    ].join('\n'));
    await vscode.window.showTextDocument(await vscode.workspace.openTextDocument(inspectionUri));
    const inspectionBreakpoint = new vscode.SourceBreakpoint(new vscode.Location(inspectionUri, new vscode.Position(6, 0)));
    vscode.debug.addBreakpoints([inspectionBreakpoint]);
    const inspectionMessages = [];
    const inspectionTracker = vscode.debug.registerDebugAdapterTrackerFactory('rukbat', {
        createDebugAdapterTracker() { return { onDidSendMessage(message) { inspectionMessages.push(message); } }; }
    });
    try {
        assert.ok(await vscode.debug.startDebugging(vscode.workspace.workspaceFolders[0], {
            type: 'rukbat', request: 'launch', name: 'Editor inspection acceptance', program: inspectionUri.fsPath, stopOnEntry: false
        }));
        await until(() => inspectionMessages.some(message => message.event === 'stopped'), 'inspection breakpoint');
        const session = vscode.debug.activeDebugSession;
        async function inspect(expectedLine, expectedCode) {
            const stack = await session.customRequest('stackTrace', { threadId: 1 });
            const frameId = stack.stackFrames[0].id;
            assert.equal(stack.stackFrames[0].line, expectedLine);
            const { scopes } = await session.customRequest('scopes', { frameId });
            const { variables } = await session.customRequest('variables', { variablesReference: scopes.find(scope => scope.name === 'Module').variablesReference });
            assert.equal(variables.find(value => value.name === 'fraction').value, '1.0000000000000002');
            const { variables: fields } = await session.customRequest('variables', { variablesReference: variables.find(value => value.name === 'result').variablesReference });
            assert.equal(fields.find(value => value.name === 'exit_code').value, expectedCode);
            for (const [expression, expected] of [['result.exit_code', expectedCode], ['copies[0].exit_code', '7'], ['fraction', '1.0000000000000002']])
                assert.equal((await session.customRequest('evaluate', { expression, frameId })).result, expected);
            assert.match((await session.customRequest('evaluate', { expression: 'options.output_encoding', frameId })).result, /^<[^>]+>$/);
        }
        await inspect(7, '7');
        for (const [stops, line, code] of [[2, 8, '7'], [3, 9, '9']]) {
            await vscode.commands.executeCommand('workbench.action.debug.stepOver');
            await until(() => inspectionMessages.filter(message => message.event === 'stopped').length === stops, 'inspection step over');
            await inspect(line, code);
        }
        await session.customRequest('continue', { threadId: 1 });
        await until(() => inspectionMessages.some(message => message.event === 'terminated'), 'inspection completion');
    } finally {
        vscode.debug.removeBreakpoints([inspectionBreakpoint]);
        inspectionTracker.dispose();
        if (vscode.debug.activeDebugSession?.type === 'rukbat') await vscode.debug.stopDebugging();
    }

    console.log('Passed VS Code references, rename/Undo, incomplete-call signature help, language features, tasks, saved debug launch, source breakpoints, variables, editor stepping, numeric/opaque inspection, and debug completion.');
}

module.exports = { run };
