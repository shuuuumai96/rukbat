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
    fs.writeFileSync(commandUri.fsPath, '//! Editor task documentation.\n//! ```rukbat check\n//! print("example")\n//! ```\nprint("task")\n');
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

    console.log('Passed VS Code diagnostics, unsaved edits, declaration/call/import documentation hover, imported definitions, file watching, completion, formatting, and restart.');
}

module.exports = { run };
