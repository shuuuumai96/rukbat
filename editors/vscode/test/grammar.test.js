'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { test, before } = require('node:test');
const textmate = require('vscode-textmate');
const oniguruma = require('vscode-oniguruma');

let grammar;
before(async () => {
    await oniguruma.loadWASM(fs.readFileSync(require.resolve('vscode-oniguruma/release/onig.wasm')).buffer);
    const registry = new textmate.Registry({
        onigLib: Promise.resolve({ createOnigScanner: patterns => new oniguruma.OnigScanner(patterns), createOnigString: text => new oniguruma.OnigString(text) }),
        loadGrammar: async () => JSON.parse(fs.readFileSync(path.join(__dirname, '../syntaxes/rukbat.tmLanguage.json'), 'utf8'))
    });
    grammar = await registry.loadGrammar('source.rukbat');
});

function scopesAt(text, offset) {
    return grammar.tokenizeLine(text).tokens.find(t => t.startIndex <= offset && offset < t.endIndex).scopes;
}

test('only // starts a comment', () => {
    assert.ok(scopesAt('// comment', 4).includes('comment.line.double-slash.rukbat'));
    assert.ok(scopesAt('# rejected', 0).includes('invalid.illegal.rukbat'));
    assert.deepEqual(require('../language-configuration.json').comments, { lineComment: '//' });
});

test('documentation has its own scope without changing ordinary comments or strings', () => {
    for (const source of ['/// Documentation', '//! Overview']) {
        assert.ok(scopesAt(source, 4).includes('comment.line.documentation.rukbat'));
    }
    assert.ok(scopesAt('//// Divider', 4).includes('comment.line.double-slash.rukbat'));
    assert.ok(scopesAt('"/// literal"', 4).includes('string.quoted.double.rukbat'));
});

test('comment markers in strings remain strings', () => {
    assert.ok(scopesAt('print("https://host/#name")', 15).includes('string.quoted.double.rukbat'));
    assert.ok(scopesAt("print('// literal')", 9).includes('string.quoted.single.rukbat'));
});

test('keywords, types, scientific numbers and escapes receive correct scopes', () => {
    assert.ok(scopesAt('const value := 1', 0).includes('storage.modifier.rukbat'));
    assert.ok(scopesAt('while ready {', 0).includes('keyword.control.rukbat'));
    assert.ok(scopesAt('value: int = 1e-3', 7).includes('storage.type.rukbat'));
    assert.ok(scopesAt('value: float = 1.2e-3', 16).includes('constant.numeric.rukbat'));
    assert.ok(scopesAt('"\\n"', 1).includes('constant.character.escape.rukbat'));
    assert.ok(scopesAt('"\\q"', 1).includes('invalid.illegal.escape.rukbat'));
});

test('physical multiline strings retain their string state', () => {
    const first = grammar.tokenizeLine('value := "first');
    const second = grammar.tokenizeLine('// still a string"', first.ruleStack);
    assert.ok(second.tokens[0].scopes.includes('string.quoted.double.rukbat'));
});

test('expected-error snippet fails outside the catchable try', () => {
    const snippet = require('../snippets/rukbat.json')['Test expected error'].body;
    assert.ok(snippet.indexOf('    test.fail("Expected Error was not thrown")') > snippet.indexOf('    }'));
    assert.ok(snippet.includes('        return'));
});

test('dictionary type constructor is highlighted without reserving a variable name', () => {
    assert.ok(scopesAt('values: dict[string, int] = {}', 8).includes('storage.type.rukbat'));
    assert.ok(!scopesAt('dict := 1', 0).includes('storage.type.rukbat'));
    const snippet = require('../snippets/rukbat.json').Dictionary.body.join('\n');
    assert.match(snippet, /dict\[/);
});
