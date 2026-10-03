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
    fs.writeFileSync(path.join(root, 'helper.rukbat'), 'fn number(): int { return 1 }\n');
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
    const definitions = await vscode.commands.executeCommand('vscode.executeDefinitionProvider', uri, new vscode.Position(1, 23));
    assert.equal((definitions[0].uri || definitions[0].targetUri).fsPath, path.join(root, 'helper.rukbat'));

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

    console.log('Passed VS Code diagnostics, unsaved edits, hover, imported definitions, file watching, completion, formatting, and restart.');
}

module.exports = { run };
