'use strict';

const { EventEmitter } = require('node:events');
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const net = require('node:net');
const { randomUUID } = require('node:crypto');

const THREAD = 1;
const STARTUP_TIMEOUT = 30000;
const BUILD_TIMEOUT = 60000;
const MAX_PROTOCOL_BUFFER = 16 * 1024 * 1024;

function normalized(file) { return path.resolve(file).toLowerCase(); }

class RukbatDebugAdapter {
    constructor(compiler, options = {}) {
        this.compiler = compiler;
        this.shell = options.shell || path.join(process.env.SystemRoot || 'C:\\Windows', 'System32/WindowsPowerShell/v1.0/powershell.exe');
        this.messages = new EventEmitter();
        this.onDidSendMessage = listener => {
            this.messages.on('message', listener);
            return { dispose: () => this.messages.off('message', listener) };
        };
        this.sequence = 1;
        this.breakpoints = new Map();
        this.handles = new Map();
        this.frames = [];
        this.mode = 'continue';
        this.configured = false;
        this.finished = false;
        this.references = 1;
    }

    send(message) { this.messages.emit('message', { seq: this.sequence++, ...message }); }
    event(event, body = {}) { this.send({ type: 'event', event, body }); }
    respond(request, body = {}) { this.send({ type: 'response', request_seq: request.seq, command: request.command, success: true, body }); }
    reject(request, error) { this.send({ type: 'response', request_seq: request.seq, command: request.command, success: false, message: error.message }); }

    handleMessage(request) {
        void this.request(request).catch(error => {
            this.reject(request, error);
            if (request.command === 'launch') this.finish();
        });
    }

    async request(request) {
        const args = request.arguments || {};
        switch (request.command) {
            case 'initialize':
                this.respond(request, { supportsConfigurationDoneRequest: true, supportsTerminateRequest: true,
                    supportsEvaluateForHovers: true, supportsDelayedStackTraceLoading: true });
                return;
            case 'launch':
                await this.launch(args);
                this.respond(request);
                this.event('initialized');
                return;
            case 'setBreakpoints': {
                const file = normalized(args.source.path);
                const requested = args.breakpoints || [];
                const points = this.map?.points.filter(point => normalized(point.file) === file) || [];
                const lines = new Set();
                const breakpoints = requested.map(breakpoint => {
                    const candidates = points.filter(point => point.line === breakpoint.line &&
                        (breakpoint.column === undefined || point.column >= breakpoint.column));
                    const point = candidates.sort((a, b) => a.column - b.column)[0];
                    if (!point || breakpoint.condition || breakpoint.hitCondition || breakpoint.logMessage)
                        return { verified: false, line: breakpoint.line, message: 'Choose an executable Rukbat statement; conditional and log breakpoints are not supported.' };
                    // A line breakpoint covers all executable statements on that line.
                    for (const candidate of (breakpoint.column === undefined ? candidates : [point])) lines.add(candidate.id);
                    // Returning a column for a line breakpoint makes VS Code
                    // draw an inline marker, for example between const and its name.
                    return { verified: true, line: point.line, source: { path: point.file },
                        ...(breakpoint.column === undefined ? {} : { column: point.column }) };
                });
                this.breakpoints.set(file, lines);
                this.respond(request, { breakpoints });
                return;
            }
            case 'configurationDone':
                this.configured = true;
                this.respond(request);
                if (this.ready) this.write({ command: 'continue' });
                return;
            case 'threads': this.respond(request, { threads: [{ id: THREAD, name: 'Rukbat' }] }); return;
            case 'stackTrace': {
                const start = args.startFrame || 0;
                const end = args.levels ? start + args.levels : undefined;
                this.respond(request, { stackFrames: this.frames.map((frame, index) => {
                    const point = this.map.points[frame.point];
                    return { id: index + 1, name: point.function, source: { name: path.basename(point.file), path: point.file },
                        line: point.line, column: point.column };
                }).slice(start, end), totalFrames: this.frames.length });
                return;
            }
            case 'scopes': {
                const frame = this.frames[args.frameId - 1];
                if (!frame) throw new Error('The stack frame is no longer available.');
                this.respond(request, { scopes: [
                    { name: 'Locals', presentationHint: 'locals', expensive: false, variablesReference: this.reference(frame.variables.filter(value => !value.global)) },
                    { name: 'Module', expensive: false, variablesReference: this.reference(frame.variables.filter(value => value.global)) }
                ] });
                return;
            }
            case 'variables': {
                const values = this.handles.get(args.variablesReference);
                if (!values) throw new Error('The variable reference is no longer available.');
                this.respond(request, { variables: values.map(value => this.variable(value)) });
                return;
            }
            case 'evaluate': {
                const frame = this.frames[(args.frameId || 1) - 1];
                const value = this.evaluate(args.expression, frame);
                const result = this.variable(value);
                this.respond(request, { result: result.value, type: result.type, variablesReference: result.variablesReference });
                return;
            }
            case 'continue': case 'next': case 'stepIn': case 'stepOut':
                if (!this.stopped) throw new Error('The program is not stopped.');
                this.mode = request.command;
                this.stepDepth = this.frames.length;
                this.stopped = false;
                this.frames = [];
                this.handles.clear();
                this.respond(request, { allThreadsContinued: true });
                this.event('continued', { threadId: THREAD, allThreadsContinued: true });
                this.write({ command: 'continue' });
                return;
            case 'pause': this.pause = true; this.respond(request); return;
            case 'disconnect': case 'terminate':
                this.respond(request); this.finish(); return;
            case 'setExceptionBreakpoints':
                if (args.filters?.length) throw new Error('Exception breakpoints are not supported.');
                this.respond(request); return;
            default: throw new Error(`Unsupported debug request: ${request.command}`);
        }
    }

    reference(values) { const id = this.references++; this.handles.set(id, values); return id; }
    variable(value) {
        return { name: value.name, value: value.value, type: value.type,
            variablesReference: value.children?.length ? this.reference(value.children) : 0,
            ...(value.isConst ? { presentationHint: { attributes: ['readOnly'] } } : {}) };
    }
    evaluate(expression, frame) {
        // Read-only names/field/index paths. Never evaluate arbitrary PowerShell
        // or invoke user functions as a side effect of a watch or hover.
        const tokens = expression?.trim().match(/^[\p{L}_][\p{L}\p{N}_]*(?:\.[\p{L}_][\p{L}\p{N}_]*|\[\d+\])*$/u);
        if (!tokens || !frame) throw new Error('Use a variable name, field, or constant array index.');
        const segments = tokens[0].match(/[\p{L}_][\p{L}\p{N}_]*|\[\d+\]/gu);
        let values = frame.variables;
        let value;
        for (const name of segments) {
            value = values?.find(item => item.name === name);
            if (!value) throw new Error('The value is unavailable at this stop (or exceeds the inspection limit).');
            values = value.children;
        }
        return value;
    }

    async launch(args) {
        if (process.platform !== 'win32') throw new Error('Rukbat debugging requires Windows PowerShell 5.1.');
        if (typeof args.program !== 'string' || path.extname(args.program).toLowerCase() !== '.rukbat')
            throw new Error('program must be a saved .rukbat file.');
        if (args.args !== undefined && (!Array.isArray(args.args) || args.args.some(item => typeof item !== 'string')))
            throw new Error('args must be an array of literal strings.');
        if (args.cwd !== undefined && typeof args.cwd !== 'string') throw new Error('cwd must be a directory path.');
        this.stopOnEntry = args.stopOnEntry !== false && !args.noDebug;
        this.noDebug = !!args.noDebug;
        const program = path.resolve(args.program);
        const cwd = path.resolve(args.cwd || path.dirname(program));
        const id = randomUUID();
        this.output = path.join(path.dirname(program), `.rukbat-debug-${id}.ps1`);
        this.temp = fs.mkdtempSync(path.join(os.tmpdir(), 'rukbat-debug-'));
        await this.build(program, cwd);
        if (this.finished) throw new Error('Debug launch was canceled.');
        this.map = JSON.parse(fs.readFileSync(this.output + '.rukbat-map.json', 'utf8'));
        if (this.map.version !== 1) throw new Error('Unsupported Rukbat debug map version.');
        const configuration = path.join(this.temp, 'launch.json');
        fs.writeFileSync(configuration, JSON.stringify({ script: this.output, map: this.output + '.rukbat-map.json', cwd, args: args.args || [] }));
        const pipeName = 'rukbat-debug-' + id;
        await new Promise((resolve, reject) => {
            this.server = net.createServer(socket => {
                this.socket = socket;
                this.server.close();
                let buffer = '';
                socket.setEncoding('utf8');
                socket.on('data', data => {
                    buffer += data;
                    if (buffer.length > MAX_PROTOCOL_BUFFER) { this.fail(new Error('Debug protocol message is too large.')); return; }
                    let newline;
                    while ((newline = buffer.indexOf('\n')) >= 0) {
                        const line = buffer.slice(0, newline); buffer = buffer.slice(newline + 1);
                        try { this.hostMessage(JSON.parse(line)); }
                        catch (error) { this.fail(error); }
                    }
                });
                socket.on('error', error => this.fail(error));
                // A normal host exit closes the pipe before Windows reports its
                // process exit code. Let the child close event own termination.
                socket.on('close', () => { if (!this.finished && !this.child) this.finish(); });
            });
            this.server.once('error', reject);
            this.server.listen('\\\\.\\pipe\\' + pipeName, resolve);
        });
        if (this.finished) throw new Error('Debug launch was canceled.');
        this.startupTimer = setTimeout(() => this.fail(new Error('Windows PowerShell debug host did not start in time.')), STARTUP_TIMEOUT);
        this.child = spawn(this.shell, ['-NoLogo', '-NoProfile', '-NonInteractive', '-File', path.join(__dirname, 'debug-host.ps1'),
            '-PipeName', pipeName, '-Configuration', configuration], { cwd, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
        this.child.stdout.setEncoding('utf8');
        this.child.stderr.setEncoding('utf8');
        this.child.stdout.on('data', output => this.event('output', { category: 'stdout', output }));
        this.child.stderr.on('data', output => this.event('output', { category: 'stderr', output }));
        this.child.once('error', error => this.fail(error));
        this.child.once('close', code => { this.event('exited', { exitCode: code ?? 1 }); this.finish(); });
    }

    build(program, cwd) {
        return new Promise((resolve, reject) => {
            const child = this.builder = spawn(this.compiler.command, [...this.compiler.args, 'build', program, '--debug', '--output', this.output],
                { cwd, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
            let diagnostics = '';
            child.stderr.setEncoding('utf8');
            child.stderr.on('data', output => { diagnostics += output; this.event('output', { category: 'stderr', output }); });
            child.stdout.on('data', () => {});
            const timer = setTimeout(() => { child.kill(); reject(new Error('Debug compilation timed out.')); }, BUILD_TIMEOUT);
            child.once('error', error => { clearTimeout(timer); reject(error); });
            child.once('close', code => {
                clearTimeout(timer); this.builder = undefined;
                if (code !== 0) reject(new Error(diagnostics.trim() || 'Debug compilation failed.'));
                else resolve();
            });
        });
    }

    write(message) { if (this.socket && !this.socket.destroyed) this.socket.write(JSON.stringify(message) + '\n'); }
    hostMessage(message) {
        if (this.finished) return;
        if (message.event === 'ready') {
            clearTimeout(this.startupTimer);
            this.ready = true;
            if (this.configured) this.write({ command: 'continue' });
        } else if (message.event === 'checkpoint') {
            const points = message.points.filter(id => id >= 0);
            const id = points[0];
            if (!this.map.points[id]) throw new Error('Invalid checkpoint from debug host.');
            let reason;
            if (!this.noDebug) {
                if (this.stopOnEntry) reason = 'entry';
                else if (this.pause) reason = 'pause';
                else if ([...this.breakpoints.values()].some(ids => ids.has(id))) reason = 'breakpoint';
                else if (this.mode === 'stepIn' || this.mode === 'next' && points.length <= this.stepDepth ||
                    this.mode === 'stepOut' && points.length < this.stepDepth) reason = 'step';
            }
            if (reason) {
                this.stopOnEntry = false; this.pause = false; this.reason = reason;
                this.write({ command: 'inspect' });
            } else this.write({ command: 'continue' });
        } else if (message.event === 'snapshot') {
            this.frames = message.frames;
            this.handles.clear();
            this.stopped = true;
            this.event('stopped', { reason: this.reason, threadId: THREAD, allThreadsStopped: true });
        }
    }
    fail(error) { this.event('output', { category: 'stderr', output: error.message + '\n' }); this.finish(); }
    finish() {
        if (this.finished) return;
        this.finished = true;
        clearTimeout(this.startupTimer);
        const closing = [];
        for (const child of [this.builder, this.child]) {
            if (child && child.exitCode === null && child.signalCode === null) {
                closing.push(new Promise(resolve => child.once('close', resolve)));
                child.kill();
            }
        }
        this.socket?.destroy();
        this.server?.close();
        this.frames = [];
        this.handles.clear();
        // Wait for Windows to release script/directory handles before reporting
        // termination or removing the exact session-owned files.
        void Promise.all(closing).then(() => {
            for (const file of [this.output, this.output && this.output + '.rukbat-map.json', this.temp && path.join(this.temp, 'launch.json')]) {
                if (file) { try { fs.unlinkSync(file); } catch { /* May already be absent. */ } }
            }
            if (this.temp) { try { fs.rmdirSync(this.temp); } catch { /* An external reader may still hold it. */ } }
            this.event('terminated');
        });
    }
    dispose() { this.finish(); }
}

module.exports = { RukbatDebugAdapter };
