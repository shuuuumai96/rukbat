'use strict';

const assert = require('node:assert/strict');
const { test } = require('node:test');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { RukbatDebugAdapter } = require('../debug-adapter');

const dll = path.resolve(__dirname, '../../../src/Rukbat/bin/Debug/net8.0/rukbat.dll');
const compiler = { command: 'dotnet', args: [dll] };

class Session {
    constructor(options) {
        this.adapter = new RukbatDebugAdapter(compiler, options);
        this.seq = 0;
        this.messages = [];
        this.waiters = [];
        this.outputs = '';
        this.adapter.onDidSendMessage(message => {
            if (message.event === 'output') this.outputs += message.body.output;
            this.messages.push(message);
            this.drain();
        });
    }
    drain() {
        for (const waiter of [...this.waiters]) {
            const index = this.messages.findIndex(waiter.predicate);
            if (index < 0) continue;
            this.waiters.splice(this.waiters.indexOf(waiter), 1);
            clearTimeout(waiter.timer);
            waiter.resolve(this.messages.splice(index, 1)[0]);
        }
    }
    wait(predicate) {
        return new Promise((resolve, reject) => {
            const waiter = { predicate, resolve, timer: setTimeout(() => {
                this.waiters.splice(this.waiters.indexOf(waiter), 1);
                reject(new Error('Timed out waiting for debugger. Output: ' + this.outputs));
            }, 20000) };
            this.waiters.push(waiter);
            this.drain();
        });
    }
    event(name) { return this.wait(message => message.event === name); }
    async request(command, args = {}) {
        const seq = ++this.seq;
        const response = this.wait(message => message.type === 'response' && message.request_seq === seq);
        this.adapter.handleMessage({ type: 'request', seq, command, arguments: args });
        const result = await response;
        if (!result.success) throw new Error(result.message);
        return result.body;
    }
    async start(program, args = {}) {
        await this.request('initialize');
        await this.request('launch', { program, ...args });
        await this.event('initialized');
    }
    async stack() { return (await this.request('stackTrace', { threadId: 1 })).stackFrames; }
    async values(frameId = 1) {
        const { scopes } = await this.request('scopes', { frameId });
        const values = [];
        for (const scope of scopes) values.push(...(await this.request('variables', { variablesReference: scope.variablesReference })).variables);
        return Object.fromEntries(values.map(value => [value.name, value]));
    }
    dispose() { this.adapter.dispose(); }
}

function project(t) {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'rukbat debug 日本語 [literal] '));
    const session = new Session();
    t.after(async () => {
        if (!session.adapter.finished) {
            session.dispose();
            await session.event('terminated');
        }
        fs.rmSync(directory, { recursive: true, force: true });
    });
    return { directory, session, write(file, source) {
        const target = path.join(directory, file); fs.writeFileSync(target, source); return target;
    } };
}

test('line breakpoints stay in the gutter while explicit column breakpoints select one statement', { skip: process.platform !== 'win32' }, async t => {
    const source = '    const first := 1; const second := 2\nprint(first + second)';
    const secondColumn = source.indexOf('second') + 1;
    for (const column of [undefined, secondColumn]) {
        const { session: s, write } = project(t);
        const main = write('main.rukbat', source);
        await s.start(main, { stopOnEntry: false });
        const { breakpoints } = await s.request('setBreakpoints', {
            source: { path: main }, breakpoints: [{ line: 1, ...(column === undefined ? {} : { column }) }]
        });
        assert.equal(breakpoints[0].verified, true);
        assert.equal(breakpoints[0].line, 1);
        assert.equal(Object.hasOwn(breakpoints[0], 'column'), column !== undefined);
        if (column !== undefined) assert.equal(breakpoints[0].column, secondColumn);
        await s.request('configurationDone');
        await s.event('stopped');
        assert.equal((await s.stack())[0].line, 1);
        if (column === undefined) {
            assert.equal((await s.values()).first, undefined);
            await s.request('continue', { threadId: 1 });
            await s.event('stopped');
            assert.equal((await s.stack())[0].line, 1);
        }
        assert.equal((await s.values()).first.value, '1');
        assert.equal((await s.values()).second, undefined);
        await s.request('continue', { threadId: 1 });
        assert.equal((await s.event('exited')).body.exitCode, 0);
        await s.event('terminated');
        assert.equal(s.outputs.trim(), '3');
    }
});

test('native PS 5.1 debugger maps imports, stack, variables, structs, arrays and all step operations', { skip: process.platform !== 'win32' }, async t => {
    const { session: s, write } = project(t);
    const helper = write('helper.rukbat', 'moduleCount := 1\nfn add(a: int): int {\n    local := a + moduleCount\n    return local\n}\n');
    const source = [
        'import "./helper.rukbat" as helper',
        'struct Data { name: string; value: int }',
        'fn outer(a: int): int {',
        '    local := a',
        '    return helper.add(local)',
        '}',
        'count := 2',
        'const data := Data { name: "日本語", value: 9223372036854775807 }',
        'items: int[] = [3, 4]',
        'total := outer(count)',
        'count = total',
        'print(count)',
        'print(args()[0])'
    ].join('\n');
    const main = write('main.rukbat', source);
    const deployed = write('main.ps1', 'PRESERVE REGULAR BUILD');
    await s.start(main, { stopOnEntry: false, args: ['a b $(literal) "quote" 日本語'] });
    const breakpoints = await s.request('setBreakpoints', { source: { path: main }, breakpoints: [{ line: 10 }, { line: 2 }] });
    assert.equal(breakpoints.breakpoints[0].verified, true);
    assert.equal(breakpoints.breakpoints[1].verified, false);
    await s.request('configurationDone');
    assert.equal((await s.event('stopped')).body.reason, 'breakpoint');
    assert.equal((await s.stack())[0].line, 10);
    let values = await s.values();
    assert.equal(values.count.value, '2');
    assert.equal(values.data.presentationHint.attributes[0], 'readOnly');
    assert.equal((await s.request('evaluate', { expression: 'data.name', frameId: 1 })).result, '"日本語"');
    assert.equal((await s.request('evaluate', { expression: 'data.value', frameId: 1 })).result, '9223372036854775807');
    assert.equal((await s.request('evaluate', { expression: 'items[1]', frameId: 1 })).result, '4');
    assert.equal((await s.request('evaluate', { expression: 'items.length', frameId: 1 })).result, '2');
    await assert.rejects(s.request('evaluate', { expression: 'count = 8' }), /Use a variable/);
    await s.request('stepIn', { threadId: 1 });
    await s.event('stopped');
    assert.equal((await s.stack())[0].line, 4);
    assert.equal((await s.values()).a.value, '2');
    assert.equal((await s.values(2)).count.value, '2');
    await s.request('next', { threadId: 1 });
    await s.event('stopped');
    assert.equal((await s.stack())[0].line, 5);
    assert.equal((await s.values()).local.value, '2');
    await s.request('stepIn', { threadId: 1 });
    await s.event('stopped');
    let stack = await s.stack();
    assert.equal(stack.length, 3);
    assert.equal(stack[0].source.path, helper);
    assert.equal(stack[0].line, 3);
    values = await s.values();
    assert.equal(values.a.value, '2');
    assert.equal(values.moduleCount.value, '1');
    await s.request('stepOut', { threadId: 1 });
    await s.event('stopped');
    assert.equal((await s.stack())[0].line, 11);
    assert.equal((await s.values()).total.value, '3');
    await s.request('next', { threadId: 1 });
    await s.event('stopped');
    assert.equal((await s.stack())[0].line, 12);
    assert.equal((await s.values()).count.value, '3');
    await s.request('continue', { threadId: 1 });
    assert.equal((await s.event('exited')).body.exitCode, 0);
    await s.event('terminated');
    assert.match(s.outputs, /3\r?\na b \$\(literal\) "quote" 日本語/);
    assert.equal(fs.readFileSync(deployed, 'utf8'), 'PRESERVE REGULAR BUILD');
    assert.ok(!fs.readdirSync(path.dirname(main)).some(file => file.startsWith('.rukbat-debug-')));
});

test('entry, loop condition reevaluation, dynamic breakpoints, catch variables and termination', { skip: process.platform !== 'win32' }, async t => {
    const { session: s, write } = project(t);
    const main = write('main.rukbat', 'count := 0\nwhile count < 2 {\n    count = count + 1\n    continue\n}\ntry {\n    throw Error { message: "test" }\n} catch failure {\n    print(failure.message)\n}\n');
    await s.start(main);
    await s.request('configurationDone');
    assert.equal((await s.event('stopped')).body.reason, 'entry');
    assert.equal((await s.stack())[0].line, 1);
    await s.request('setBreakpoints', { source: { path: main }, breakpoints: [{ line: 2 }, { line: 9 }] });
    for (const count of ['0', '1', '2']) {
        await s.request('continue', { threadId: 1 });
        await s.event('stopped');
        assert.equal((await s.stack())[0].line, 2);
        assert.equal((await s.values()).count.value, count);
    }
    await s.request('continue', { threadId: 1 });
    await s.event('stopped');
    assert.equal((await s.stack())[0].line, 9);
    assert.equal((await s.request('evaluate', { expression: 'failure.message' })).result, '"test"');
    const output = s.adapter.output;
    await s.request('terminate');
    await s.event('terminated');
    assert.equal(fs.existsSync(output), false);
});

test('next skips a user function', { skip: process.platform !== 'win32' }, async t => {
    const { session: s, write } = project(t);
    const main = write('main.rukbat', 'fn number(): int {\n    value := 1\n    return value\n}\nresult := number()\nprint(result)\n');
    await s.start(main);
    await s.request('configurationDone');
    await s.event('stopped');
    assert.equal((await s.stack())[0].line, 5);
    await s.request('next', { threadId: 1 });
    await s.event('stopped');
    assert.equal((await s.stack())[0].line, 6);
    await s.request('continue', { threadId: 1 });
    await s.event('terminated');
});

test('recursive calls retain distinct locals and live module variable references', { skip: process.platform !== 'win32' }, async t => {
    const { session: s, write } = project(t);
    const main = write('main.rukbat', [
        'counter := 0',
        'fn recurse(n: int): int {',
        '    local := n',
        '    if n == 0 {',
        '        counter = 10',
        '        return n',
        '    }',
        '    result := recurse(n - 1)',
        '    return result + local',
        '}',
        'total := recurse(2)',
        'print(total)'
    ].join('\n'));
    await s.start(main, { stopOnEntry: false });
    await s.request('setBreakpoints', { source: { path: main }, breakpoints: [{ line: 6 }] });
    await s.request('configurationDone');
    await s.event('stopped');
    assert.equal((await s.stack()).length, 4);
    for (const [frame, expected] of [[1, '0'], [2, '1'], [3, '2']]) {
        const values = await s.values(frame);
        assert.equal(values.local.value, expected);
        assert.equal(values.n.value, expected);
        assert.equal(values.counter.value, '10');
    }
    assert.equal((await s.values(4)).counter.value, '10');
    await s.request('continue', { threadId: 1 });
    assert.equal((await s.event('exited')).body.exitCode, 0);
    await s.event('terminated');
    assert.match(s.outputs, /3/);
});

test('methods, shadowed bindings, nullable arrays and breakpoint interruption of next', { skip: process.platform !== 'win32' }, async t => {
    const { session: s, write } = project(t);
    const main = write('main.rukbat', [
        'struct Counter {',
        '    count: int',
        '}',
        'impl Counter {',
        '    fn increment(): void {',
        '        self.count = self.count + 1',
        '        print(self.count)',
        '    }',
        '}',
        'value := Counter { count: 1 }',
        'optional: string?[] = [null, "test"]',
        'value.increment()',
        'if true {',
        '    value := 8',
        '    print(value)',
        '}',
        'print(value.count)'
    ].join('\n'));
    await s.start(main, { stopOnEntry: false });
    await s.request('setBreakpoints', { source: { path: main }, breakpoints: [{ line: 12 }, { line: 7 }, { line: 15 }, { line: 17 }] });
    await s.request('configurationDone');
    await s.event('stopped');
    assert.equal((await s.request('evaluate', { expression: 'optional[0]' })).result, 'null');
    assert.equal((await s.request('evaluate', { expression: 'optional[1]' })).result, '"test"');
    await s.request('next', { threadId: 1 });
    assert.equal((await s.event('stopped')).body.reason, 'breakpoint');
    assert.equal((await s.stack())[0].name, 'Counter.increment');
    assert.equal((await s.request('evaluate', { expression: 'self.count' })).result, '2');
    await s.request('continue', { threadId: 1 });
    await s.event('stopped');
    assert.equal((await s.values()).value.value, '8');
    await s.request('continue', { threadId: 1 });
    await s.event('stopped');
    assert.equal((await s.request('evaluate', { expression: 'value.count' })).result, '2');
    await s.request('continue', { threadId: 1 });
    await s.event('terminated');
});

test('pause stops at the next checkpoint and terminate ends a waiting native call', { skip: process.platform !== 'win32' }, async t => {
    const { session: s, write } = project(t);
    const main = write('main.rukbat', 'powershell<void>("Start-Sleep", Milliseconds: 300)\nprint("after pause")\npowershell<void>("Start-Sleep", Seconds: 30)\nprint("must not run")');
    await s.start(main, { stopOnEntry: false });
    await s.request('configurationDone');
    await s.request('pause', { threadId: 1 });
    assert.equal((await s.event('stopped')).body.reason, 'pause');
    await s.request('setBreakpoints', { source: { path: main }, breakpoints: [{ line: 3 }] });
    await s.request('continue', { threadId: 1 });
    await s.event('stopped');
    assert.equal((await s.stack())[0].line, 3);
    await s.request('continue', { threadId: 1 });
    await s.request('terminate');
    await s.event('terminated');
    assert.doesNotMatch(s.outputs, /must not run/);
});

test('run without debugging honors arguments and ignores entry/breakpoints', { skip: process.platform !== 'win32' }, async t => {
    const { session: s, write } = project(t);
    const main = write('main.rukbat', 'print(args()[0])');
    await s.start(main, { noDebug: true, args: ['literal'] });
    await s.request('setBreakpoints', { source: { path: main }, breakpoints: [{ line: 1 }] });
    await s.request('configurationDone');
    assert.equal((await s.event('exited')).body.exitCode, 0);
    await s.event('terminated');
    assert.equal(s.messages.some(message => message.event === 'stopped'), false);
    assert.match(s.outputs, /literal/);
});

test('compile errors never launch PowerShell or execute existing output', { skip: process.platform !== 'win32' }, async t => {
    const { session: s, write } = project(t);
    const main = write('main.rukbat', 'value: int = "invalid"');
    const deployed = write('main.ps1', '[Console]::WriteLine("STALE")');
    await s.request('initialize');
    await assert.rejects(s.request('launch', { program: main }), /RUK2001/);
    await s.event('terminated');
    assert.equal(s.adapter.child, undefined);
    assert.equal(fs.readFileSync(deployed, 'utf8'), '[Console]::WriteLine("STALE")');
    assert.doesNotMatch(s.outputs, /STALE/);
});

test('runtime failures and explicit exit codes survive the debug host', { skip: process.platform !== 'win32' }, async t => {
    for (const [source, expected] of [['exit(7)', 7], ['throw Error { message: "expected failure" }', 1]]) {
        const p = project(t);
        const main = p.write('main.rukbat', source);
        await p.session.start(main, { stopOnEntry: false });
        await p.session.request('configurationDone');
        assert.equal((await p.session.event('exited')).body.exitCode, expected);
        await p.session.event('terminated');
        if (expected === 1) assert.match(p.session.outputs, /expected failure/);
    }
});

test('inspection formats bound integer fields and opaque standard values consistently', { skip: process.platform !== 'win32' }, async t => {
    const { session: s, write } = project(t);
    const main = write('main.rukbat', [
        'import encoding',
        'import process',
        'selected := encoding.utf8',
        'options := process.options(5000, selected)',
        'result := process.run("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "[Environment]::Exit(7)"], options)',
        'copied := result',
        'codes: int[] = [result.exit_code]',
        'print(result.exit_code)'
    ].join('\n'));
    await s.start(main, { stopOnEntry: false });
    await s.request('setBreakpoints', { source: { path: main }, breakpoints: [{ line: 8 }] });
    await s.request('configurationDone');
    await s.event('stopped');
    for (const expression of ['result.exit_code', 'copied.exit_code', 'codes[0]'])
        assert.equal((await s.request('evaluate', { expression })).result, '7', expression);
    const values = await s.values();
    const fields = (await s.request('variables', { variablesReference: values.result.variablesReference })).variables;
    assert.equal(fields.find(value => value.name === 'exit_code').value, '7');
    assert.equal(values.selected.value, '<' + values.selected.type + '>');
    assert.equal((await s.request('evaluate', { expression: 'options.output_encoding' })).result, values.selected.value);
    await s.request('continue', { threadId: 1 });
    assert.equal((await s.event('exited')).body.exitCode, 0);
    await s.event('terminated');
    assert.match(s.outputs, /7/);
});

test('file line descriptors stay opaque while a traversal is paused', { skip: process.platform !== 'win32' }, async t => {
    const { session: s, write } = project(t);
    const input = write('lines.txt', 'first\nsecond\n');
    const main = write('main.rukbat', [
        'import fs; import encoding',
        `source := fs.lines(${JSON.stringify(input.replace(/\\/g, '/'))}, encoding.utf8)`,
        'for line in source {',
        '    print(line)',
        '    break',
        '}',
        'print("done")'
    ].join('\n'));
    await s.start(main, { stopOnEntry: false });
    await s.request('setBreakpoints', { source: { path: main }, breakpoints: [{ line: 4 }] });
    await s.request('configurationDone');
    await s.event('stopped');
    const values = await s.values();
    assert.equal(values.source.type, 'fs.Lines');
    assert.equal(values.source.value, '<fs.Lines>');
    assert.equal(values.source.variablesReference, 0);
    assert.match(values.line.value, /first/);
    const invalid = await s.request('evaluate', { expression: 'source.Path' }).catch(error => error);
    assert.ok(invalid instanceof Error || !('result' in invalid));
    await s.request('continue', { threadId: 1 });
    assert.equal((await s.event('exited')).body.exitCode, 0);
    await s.event('terminated');
    assert.match(s.outputs, /first/);
    assert.match(s.outputs, /done/);
    fs.renameSync(input, input + '.released');
});

test('logging configuration and levels have readable debugger values', { skip: process.platform !== 'win32' }, async t => {
    const { session: s, write } = project(t);
    const main = write('main.rukbat', [
        'import log',
        'const logger := log.create()',
        'levels: log.Level[] = [log.level_debug, log.level_info, log.level_warn, log.level_error]',
        'disabled: log.Level? = null',
        'selected: log.Level? = log.level_warn',
        'print(log.enabled(logger, levels[1]))'
    ].join('\n'));
    await s.start(main, { stopOnEntry: false });
    await s.request('setBreakpoints', { source: { path: main }, breakpoints: [{ line: 6 }] });
    await s.request('configurationDone');
    await s.event('stopped');
    const values = await s.values();
    assert.equal(values.logger.type, 'log.Logger');
    assert.equal(values.logger.value, 'log.Logger');
    assert.equal(values.logger.presentationHint.attributes[0], 'readOnly');
    assert.equal((await s.request('evaluate', { expression: 'logger.stderr_level' })).result, 'log.level_info');
    assert.equal((await s.request('evaluate', { expression: 'logger.file_level' })).result, 'log.level_info');
    assert.equal((await s.request('evaluate', { expression: 'logger.file' })).result, 'null');
    assert.equal(values.disabled.value, 'null');
    assert.equal(values.selected.type, 'log.Level?');
    assert.equal(values.selected.value, 'log.level_warn');
    for (const [index, name] of ['debug', 'info', 'warn', 'error'].entries()) {
        const level = await s.request('evaluate', { expression: `levels[${index}]` });
        assert.equal(level.type, 'log.Level');
        assert.equal(level.result, 'log.level_' + name);
    }
    const fields = (await s.request('variables', { variablesReference: values.logger.variablesReference })).variables;
    assert.deepEqual(fields.map(field => field.name), ['stderr_level', 'file', 'file_level']);
    const invalid = await s.request('evaluate', { expression: 'levels[0].Tag' }).catch(error => error);
    assert.ok(invalid instanceof Error || !('result' in invalid));
    await s.request('continue', { threadId: 1 });
    assert.equal((await s.event('exited')).body.exitCode, 0, s.outputs);
    await s.event('terminated');
    assert.equal(s.outputs.trim(), 'True');
});

test('float inspection preserves the stored binary64 value', { skip: process.platform !== 'win32' }, async t => {
    const { session: s, write } = project(t);
    const main = write('main.rukbat', 'value := 1.0000000000000002\nprint(value)');
    await s.start(main, { stopOnEntry: false });
    await s.request('setBreakpoints', { source: { path: main }, breakpoints: [{ line: 2 }] });
    await s.request('configurationDone');
    await s.event('stopped');
    assert.equal((await s.values()).value.value, '1.0000000000000002');
    assert.equal((await s.request('evaluate', { expression: 'value' })).result, '1.0000000000000002');
    await s.request('continue', { threadId: 1 });
    await s.event('terminated');
});

test('dictionary inspection preserves order and supports literal keys without executing code', { skip: process.platform !== 'win32' }, async t => {
    const { session: s, write } = project(t);
    const main = write('main.rukbat', [
        'd := {"a": [1], "A": [2], "a\\\"b": [3]}',
        'numbers := {-9223372036854775808: "min", -1: "negative", 9223372036854775807: "max"}',
        'print(d.length)'
    ].join('\n'));
    await s.start(main, { stopOnEntry: false });
    await s.request('setBreakpoints', { source: { path: main }, breakpoints: [{ line: 3 }] });
    await s.request('configurationDone');
    await s.event('stopped');
    const values = await s.values();
    assert.equal(values.d.type, 'dict[string, int[]]');
    assert.equal(values.d.value, 'dict[string, int[]] (3)');
    const children = (await s.request('variables', { variablesReference: values.d.variablesReference })).variables;
    assert.deepEqual(children.slice(0, 2).map(child => child.name), ['["a"]', '["A"]']);
    assert.equal(children.at(-1).name, 'length');
    for (const [expression, expected] of [['d["a"][0]', '1'], ["d['A'][0]", '2'], ['d["a\\\"b"][0]', '3'],
        ['numbers[-9223372036854775808]', '"min"'], ['numbers[-1]', '"negative"'], ['numbers[9223372036854775807]', '"max"'], ['d.length', '3']])
        assert.equal((await s.request('evaluate', { expression })).result, expected);
    await assert.rejects(s.request('evaluate', { expression: 'd.keys()' }), /constant array\/dictionary index/);
    await assert.rejects(s.request('evaluate', { expression: 'd["missing"]' }), /unavailable/);
    await s.request('continue', { threadId: 1 });
    assert.equal((await s.event('exited')).body.exitCode, 0);
    await s.event('terminated');
});

module.exports = { Session };
