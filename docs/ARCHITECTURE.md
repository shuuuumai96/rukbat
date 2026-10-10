# Rukbat Architecture

## Compiler stages

```text
Source → Lexer → Parser → AST → Module loading → Binding / Type checking → PowerShell
```

| Component | Responsibility |
|---|---|
| Lexer | Tokens, comments, and source ranges. |
| Parser | AST and optional tooling syntax facts; no broad semantic validation. |
| ModuleLoader | Import resolution, canonical file identity, dependency order, cycle detection. |
| TypeChecker / Symbols | Binding, scopes, types, nullable flow, call compatibility, and return paths. Binding has no separate resolver or bound-tree stage. |
| Emitter | Generate from validated syntax and type information; no name resolution or type inference. |
| Compiler | Coordinate stages and validate generated PowerShell syntax. Target compatibility requires execution tests. |

Const analysis propagates receiver mutations through calls to a fixed point,
including recursion. Copied arguments and locals do not forward mutations.
`BoundTypes` retains resolved identities inside containers for editor references.

## Source layout

The compiler is one project. Directories separate responsibilities; partial files
share stage state rather than defining independent pipeline stages.

| Location under `src/Rukbat` | Responsibility |
|---|---|
| `Compiler.cs`, `CompilationResult.cs`, `Diagnostic.cs`, `Source.cs` | Public compilation entry points, results, and source locations |
| `Program.cs`, `Commands/` | Process entry point, CLI commands, distribution probes, test execution |
| `Syntax/` | Tokens, AST, lexing, parsing |
| `Modules/` | Loaded modules, import resolution, dependency ordering |
| `Semantics/` | Symbols, bound types, binding, type compatibility, flow and expression checking |
| `StandardLibrary/` | Canonical standard modules, struct definitions, fixed call signatures and validated constants |
| `CodeGeneration/` | PowerShell traversal, expression/statement lowering, standard-call adapters, debug maps and resource emission |
| `Runtime/PowerShell/` | Fixed PowerShell helper and operation fragments embedded in generated scripts |
| `Runtime/Native/` | Embedded C# helper sources compatible with Windows PowerShell 5.1 |
| `Formatting/`, `Documentation/`, `LanguageServer/` | Formatter, documentation and editor tooling |

TypeChecker and Emitter main files own stage state and orchestration. Binding and
checking cooperate during one traversal; parsed type names use ValueType until resolved.

For a fixed standard API, keep parameter and result types together in
`StandardLibrary/StandardSignatures.cs`. Datetime operations reuse DateTimeValues' existing
operation definitions. Generic array and JSON operations need context-dependent checking and
remain in Semantics. Module declarations and implicit dependencies belong to StandardModules;
dependency traversal belongs to ModuleLoader. Editor documentation still comes from STDLIB.md.

Edit fixed runtime code in its source language, not in C# raw string literals. PowerShell files
are fragments (some are switch arms), not standalone scripts. RuntimeResources embeds and reads
them without requiring loose files beside the compiler. CodeGeneration owns dynamic substitutions,
bound field keys and emitted indentation. Native files retain the `.cs.txt` suffix so they are not
accidentally compiled into the authoring tool; ProcessJob is explicitly compiled for the test runner
as well as embedded for generated scripts. Moving these files must preserve both resource names and
the operations toolkit's ProcessJob copy path.

Structural changes should preserve generated text, diagnostics and debug maps. Compare representative
ordinary and debug compilations before and after a refactor, then run semantic/editor tests and
execute affected helpers on Windows PowerShell 5.1 and PowerShell 7.

## Shared tooling

| Tool | Use of compiler stages |
|---|---|
| `check` | Load and type-check; no emission or PowerShell parsing. |
| `fmt` | Parse and normalize tokens; no binding. `FormattingSyntax` uses optional parser facts for grouping, continuations, braces, and comments. |
| `doc` | Attach comments once with ModuleDocumentation; DocumentationCatalog resolves links/signatures. Check opt-in examples, then render CommonMark HTML without emission or execution. |
| `lsp` | LanguageAnalysis reuses the front end with unsaved buffers. Bound symbols supply navigation and hover; the embedded STDLIB catalog supplies API documentation. |
| `test` | Check signatures and generate all selected entrypoints before execution. Initialize modules, invoke one function, and write a private completion marker only after normal return. A process exit alone is not success. |

EditorSymbolId identifies a declaration by path, offset, and kind independently
of display types or nullable narrowing. EditorWorkspace discovers closed callers.
References map checked comment examples back to source; rename rechecks proposed
edits and compares original bindings. Signature help analyzes a temporary copy
for incomplete calls without changing diagnostics for the real source.

The test runner contains each host in a Windows process job. `ProcessJob.cs.txt`
is compiled into the CLI and embedded for generated `process.run` scripts.

## Generated runtime

PowerShell helpers implement value copies, bounds, typed imports, explicit returns,
and errors. Only imported standard-library runtime sections are included; selection
is by module rather than individual function. A generated `.ps1` needs neither the
compiler nor loose Rukbat libraries. It rejects non-FullLanguage sessions before
user operations and does not change execution policy.

The entry wrapper captures working/script directories before module initialization.
Runtime closures resolve paths against that context, independent of later PowerShell
or .NET location changes. Runtime source labels are relative to the entry source;
compiler diagnostics retain actual source paths. See [path semantics](language.md#powershell-boundary).

Fixed PowerShell fragments live in `Runtime/PowerShell`; native C# 5-compatible
sources live in `Runtime/Native`. Lazy Add-Type loaders use the target's existing
PowerShell/.NET facilities. Process containment is established during child creation.
Move journals flush before renames, and locked handles preserve file identity.

### Dictionaries

Parsed entries retain key/value expressions. The checker owns K/V restrictions,
inference, duplicate literal keys, const/mutation rules, recursive type cycles, and
external-boundary admission. Terminal indexed writes capture receiver and key before
the RHS; intermediate indexes are strict reads. Helpers borrow receiver storage;
ordinary value boundaries clone it.

Storage is a private tagged wrapper around `Dictionary<string,object>` with
`StringComparer.Ordinal`, or `Dictionary<long,object>`, plus a `LinkedList<object>`
for insertion order. Entries own their value and order node. Lookup/update/removal
are average O(1), length O(1), and keys/copies O(n) plus recursive value costs.
User keys never become PowerShell properties. Clone recognizes the wrapper before
struct hashtables; immutable `cli.Result` storage is distinct. Dictionaries alone
need no Add-Type. JSON schemas pass the dictionary helper across module scopes
and canonicalize keys in ordinal order.

### Standard-library implementation

API contracts belong in [STDLIB.md](STDLIB.md). The implementation boundaries are:

| Area | Implementation |
|---|---|
| `fs`, `path` | An fs import emits file/directory operations, with lazy native helpers for recoverable moves and exclusive temporary entries. A path-only import emits neither filesystem operations nor those helpers. |
| `cli` | Ordinary definition structs are adapted by CliEmission. Result uses opaque immutable storage excluded from JSON, PowerShell, and debugger field inspection. Helpers need no native loader and never print, exit, or run user operations during parsing. |
| `json` | One parser serves typed and schema-free reads; `as<T>` reuses typed conversion. Immutable nodes share untouched subtrees; updates copy changed nodes. Typed replacements use the encoder. Returned arrays follow value-copy rules. |
| `csv` | Table is a bound struct; immutable Row shares an ordinal column lookup. Raw/table parsing and quoting are shared. Field keys are adapted at emission; recursive external-boundary checks exclude Row. |
| `test` | Normal Context struct plus emitted assertions. Direct primitive comparisons capture operands once with ordinary operator lowering; other expressions preserve short-circuit/nullable behavior. No fs/JSON/testing runtime dependency. |
| `process` | Bound Options/Input/EnvironmentVariable fields are adapted once. Child overrides overlay a native environment snapshot, preserving internal drive entries. Bounded input is encoded before launch; stdin and both readers share one job deadline. Internal Run/RunBytes wrappers retain closed stdin and inherited environments. |
| `encoding` | EncodingValues centralizes names, descriptions, and codecs. Validated values lower to private tags. Managed Unicode and strict native Windows codecs reject invalid bytes, unencodable characters, and best-fit substitutions. An encoding-only import needs no process/move helper. |
| Numbers | Parser checks literal bounds, checker rejects mixed arithmetic, and the Numbers helper provides checked integer operations/conversions without PowerShell promotion. Emitted only when needed. |
| `datetime` | DateTimeValues shares identities, signatures, calls, and the fixed zone catalog with the editor. Zones resolve on use, not during compilation. Immutable storage uses checked ticks; patterns are validated/sanitized, and ambiguous/nonexistent clocks or clamping conversions are rejected. An import, including fs metadata's dependency, emits the helper. |
| `array` | Checker infers element/key types. Stable sorting uses source indexes and original-index tie breaks, with ordinal/typed comparison. Emission copies values; runtime checks bounds/key lengths. No input mutation or language callbacks. |
| `hash` | HashValues shares validated identities and boundary checks. The helper streams standard cryptographic operations and uses canonical strict text encoding. |
| `zip` | Bound Options/Entry fields are adapted. Metadata, including ZIP64, is bounded before ZipArchive materialization; raw names use strict codecs. Extraction checks sizes/CRC and publishes a sibling stage without replacement. Loads the installed compression assembly. |
| `http` | HttpValues shares identities/constants; HttpEmission adapts struct fields and evaluates expressions once. System.Net.Http handles bounded immutable body bytes with reusable clients, explicit proxy/credentials, one deadline, and response disposal. Redirects are controlled explicitly; downloads stage and publish without replacement. Non-200 bodies use bounded memory. |

Process byte capture and file decoding share the encoding selector. The CLI runner
and operations toolkit use the process helper's UTF-8 wrapper. Opaque validated
types reuse qualified-name binding and recursive external-boundary checks rather
than new parser syntax. ZIP, hash, and HTTP emit only their respective helpers;
they do not pull in process or move runtimes.

## Debugging

Optional debug emission reuses bound symbols for executable line points and type
metadata. Scoped PSVariable references in an active frame stack work around
PowerShell 5.1 GetFrameVariables omissions during breakpoint actions. Generated
try/finally unwinds function/module frames; while checkpoints precede each condition.
Ordinary emission contains no debugger state or absolute development paths.

The VS Code DAP adapter compiles a session-owned artifact and controls native
PowerShell 5.1 breakpoint actions through a local named pipe. Statement/depth filters
skip helpers. Inspection uses bound field keys, preserves exact integer text, and
reads only name/field/index paths without executing user code. See [DEBUGGING.md](DEBUGGING.md).

## Authoring distribution

`tools/package.ps1` creates the CLI ZIP and VSIX from the same version-checked,
untrimmed, self-contained Windows x64 publish. The extension launches its executable
by absolute path; an external override applies to both LSP and tasks. Tasks use
ProcessExecution with literal argument arrays, workspace trust, and save-before-run.
The bounded environment probe changes no policy. This authoring runtime is not
copied into generated scripts. See [DISTRIBUTION.md](DISTRIBUTION.md).
