# Rukbat Architecture

Rukbat validates source code before generating PowerShell.

```text
Source
  ↓
Lexer
  ↓
Parser
  ↓
Syntax Tree / AST
  ↓
Module loading
  ↓
Name resolution / binding
  ↓
Type checking
  ↓
PowerShell generation
```

Each stage has a separate responsibility:

- Lexer splits strings, comments, and operators into tokens.
- Parser builds the syntax tree without type checking or name resolution.
- ModuleLoader discovers modules, resolves import paths, determines canonical module identity, builds the dependency graph, and detects cycles.
- Resolver registers symbols, manages scopes, resolves name references including module-qualified names, and rejects redeclarations.
- TypeChecker validates type compatibility, explicit numeric conversion contracts, nullable flow narrowing, function call signatures, and control-flow return semantics.
- Const is stored on variable declarations and bound symbols. TypeChecker checks writes through bound variable paths and propagates receiver mutations through bound method calls after checking all bodies. This fixed-point analysis covers indirect calls and recursion; ordinary copied arguments and locals do not forward receiver mutation. Emitter uses the existing initialization and copy operations unchanged. LSP and documentation signatures retain the const modifier.
- Emitter consumes validated syntax trees and type information to generate standalone Windows PowerShell 5.1-compatible `.ps1` files. It does not infer semantics or resolve names.
- Compiler invokes these stages in order. It validates the generated PowerShell syntax as a generator sanity check; Windows PowerShell 5.1 compatibility is established by the execution test suite, not by this parser validation alone.
- `rukbat check` stops after ModuleLoader, Resolver, and TypeChecker. It invokes neither Emitter nor the PowerShell parser.
- `rukbat test` discovers source files in the CLI, uses the same front end to bind test function signatures, then emits and validates every selected entrypoint before execution. Emitter initializes the ordinary module graph, invokes one validated function with an optional bound `test.Context`, and writes a private completion marker only after that function returns. The runner captures each PowerShell host in a separate Windows process job; process exit alone does not imply test success. The same native `ProcessJob.cs.txt` is compiled into the CLI and embedded for deployed `process.run` scripts.
- Formatter validates syntax, then normalizes tokens while preserving comments. It does not check types or resolve imports.
- Lexer retains source comment ranges outside the token stream; Parser records declaration starts. ModuleDocumentation attaches comments to parsed declarations once. DocumentationCatalog resolves user-symbol links and reuses bound type information for signatures. `rukbat doc` uses the normal checking front end and a bundled CommonMark parser, validates independent opt-in examples without execution, then renders offline HTML without calling Emitter. LSP hover, completion, and documentation warnings share the same attachment and reference model. Standard-library documentation remains owned by STDLIB.md.
- `rukbat lsp` runs a stdio JSON-RPC server. LanguageAnalysis uses the same ModuleLoader, Parser, and TypeChecker as the compiler, with an in-memory source reader for open documents. Optional parser block spans describe lexical scopes; bound symbol identities provide hover and definitions without rebinding names by spelling. Completion uses those scopes and the embedded canonical STDLIB.md signatures/documentation. Formatting calls the existing Formatter and returns edits. No backend or user code is executed. The VS Code client and TextMate grammar live under `editors/vscode`.
- During formatting, Parser also records syntax facts such as continuation newlines, struct-literal closing braces, unary operators, type-argument delimiters, and adjacent statement kinds within each block. FormattingSyntax identifies required group boundaries, and Formatter uses these facts instead of guessing expression roles from token spelling. Ordinary compilation does not collect these optional facts.

Generated scripts include small PowerShell helpers implementing Rukbat value copies, array bounds, error propagation, and PowerShell boundaries. Only imported standard-library runtime sections are included. Deployment requires neither the Rukbat compiler nor a separate Rukbat runtime.

The authoring distribution uses an untrimmed self-contained Windows x64 publish.
tools/package.ps1 builds the CLI ZIP and VSIX from the same version-checked output.
The extension launches its bundled executable by absolute path; an explicit external
override applies equally to LSP and editor tasks. Tasks use ProcessExecution with
literal argument arrays, workspace trust, and save-before-execution behavior.
Distribution owns CLI version reporting and a bounded local PowerShell capability
probe; it makes no deployment or policy changes. This bundled authoring runtime is
not copied into generated scripts.

Optional debug emission reuses checked variable, function, and field symbols to
produce executable source/generated-line points and type metadata. Debug builds
retain scoped PSVariable references in an active frame stack; this avoids the
PowerShell 5.1 GetFrameVariables omission during breakpoint actions. Function and
module frames unwind with generated try/finally, including returns and errors.
While checkpoints precede each condition evaluation. Ordinary emission remains
free of debugger state and absolute source paths. The VS Code inline DAP adapter
compiles a session-owned artifact, starts the installed Windows PowerShell 5.1
host, and controls native line breakpoint actions over a local named pipe.
Statement/depth filtering skips backend helpers during stepping. Inspection uses
bound field keys, preserves exact integer text, and evaluates only read-only
name/field/index paths without executing user code. See DEBUGGING.md for limits.

The `fs` standard module owns both file and directory operations; `path` owns path-value operations.
An `fs` import emits both file and directory implementations. A `path` import alone emits neither
filesystem operations nor the native move helper. Selection is by imported module, not by individual
function.

`json.Value` is a standard-module type bound through the existing type-name infrastructure. It is
opaque to Rukbat source: its schema and storage are emitted as a JSON value rather than a user
struct. The JSON parser is shared by typed and schema-free parsing; `json.as<T>` uses the same
conversion as `json.parse<T>`. Immutable JSON nodes may share storage safely, while arrays returned
by `json.items` and `json.keys` follow ordinary Rukbat copy semantics. Only a `json` import emits
the JSON parser, inspection operations, and updates. Updates copy the changed object or array and
share untouched immutable subtrees. Typed replacements use the existing JSON encoder before
becoming JSON nodes; JSON values are retained directly.

`csv.Table` uses an ordinary bound struct with column and row arrays. `csv.Row` is opaque,
emitted as an immutable row with a shared ordinal column lookup. Table and raw parsing share
one CSV parser; both serializers share one quoting implementation. The checker excludes CSV rows
from JSON conversion and PowerShell results, including nested containers. The emitter adapts
table results to actual bound field keys. Only a `csv` import emits CSV helpers.

Native Windows helpers for contained process execution and journaled file moves live in `Runtime/*.cs.txt`, embedded as compiler resources and copied into the generated script. `NativeEmission` emits lazy `Add-Type` loaders; the deployment machine uses its existing PowerShell/.NET facilities. These helpers use C# syntax compatible with the Windows PowerShell 5.1 compiler. Process job assignment is part of process creation, so host termination cannot leave a child executing outside the job. Move journals are flushed before renames; locked file handles preserve identity across moves and recovery.

Generated scripts reject non-FullLanguage sessions before user operations. They do not change or bypass the machine's execution/security policy.

The `test` module contributes a normal bound Context struct and compiler-emitted
assertion calls; it does not require the filesystem, JSON parser, or a separate
testing runtime. Direct primitive comparison assertions capture operands once and
use the same operator lowering as ordinary expressions. Other assertions use the
ordinary expression emitter, preserving short-circuit and nullable-array behavior.

The entry wrapper captures working and script directories before module initialization. Standard-library runtime closures retain this path context and resolve paths explicitly; they do not depend on later `$PWD` or `Environment.CurrentDirectory` values. Emitter derives runtime source labels relative to the entry source directory while leaving compiler diagnostic paths unchanged.

File and process output decoding share the canonical encoding selector and strict native Windows code-page codec. The native process helper captures bounded byte streams; `process.run` decodes them using its explicit encoding. The CLI test runner and operations toolkit use the helper's UTF-8 wrapper for their own controlled hosts.

Process Options, Input and EnvironmentVariable are ordinary bound standard structs. The emitter
normalizes their private field keys once into the backend call shape. Child environment changes
overlay a native Windows environment snapshot, preserving inherited internal drive entries,
without modifying the parent. Native stdin writing and both output readers run concurrently
under one process-job deadline. Preparation encodes bounded input before creating the child.
The CLI's existing internal Run/RunBytes wrappers retain closed input and inherited environments.

`encoding.Encoding` is a validated standard-library type bound through existing qualified type
syntax. `EncodingValues` supplies its member names, external labels, editor descriptions, and
runtime codec constructors. The checker records bound read-only members; the emitter lowers
validated values to private string tags. `encoding.parse` and `encoding.from_code_page` validate external names and numbers on the target.
Managed strict Unicode codecs handle UTF-8/16/32; native Windows conversion flags reject
invalid bytes, unencodable characters, and best-fit substitutions for legacy code pages. JSON and PowerShell boundaries reject this type recursively. An `encoding`
import alone emits conversion operations without filesystem/process native helpers. No parser
syntax or deployment runtime is added.

Integer values use signed 64-bit storage. Parser validates literal bounds (including the signed
minimum), and the checker rejects mixed numeric operations and implicit widening. Checked integer
arithmetic and explicit math conversions use the embedded Numbers helper, avoiding PowerShell
automatic promotion and floating-point integer division. It is emitted only when needed.

Datetime opaque types bind through existing qualified type syntax. DateTimeValues centralizes
type identities, standard signatures, a fixed named-zone catalog, and emitted calls.
The checker and editor share that catalog; the emitter resolves a validated zone constant
through the existing runtime lookup only at its point of use. Compilation does not inspect
the compiler host's time-zone database. The embedded C# 5-compatible DateTime
helper stores immutable UTC instants, dates, durations and Windows zones. Checked integer ticks
avoid floating-point date arithmetic. Comparison lowering is shared by expressions and test
assertions. Format patterns are validated and sanitized before invariant parsing/formatting;
ambiguous/nonexistent local clocks and range-clamping conversions are rejected explicitly.
A datetime import, including the fs metadata dependency, emits this helper. JSON and PowerShell boundaries reject these values
recursively, leaving external representations to explicit conversion functions.

The array standard module infers result element types from its checked input array. Sort keys
have a small fixed set of orderable types; keyed sorting accepts a separate key array rather
than language callbacks or general generics. The emitter uses ordinary value copies and
private runtime operations for bounds and key-length checks. The C# 5-compatible Array helper
sorts source indexes with ordinal/typed key comparisons and the original index as a stable
tie breaker. No input storage is mutated.

Hash algorithm identities use a central HashValues catalog and the existing qualified opaque-type
binding, equality, read-only-member and recursive external-boundary checks. The embedded C# 5 Hash
helper uses standard cryptographic streaming; text bytes come from the canonical strict selector.
ZIP Options/Entry use ordinary bound structs and emitter field-key adaptation. The C# 5 Zip helper
bounds and validates central/local metadata (including ZIP64) before ZipArchive materializes its
collection. It decodes raw names with the shared strict codecs, independent of runtime name-encoding
precedence, and uses ZipArchive only for compression/decompression. Extraction independently checks
stream sizes and CRC and publishes a sibling stage with a non-replacing rename. Windows PowerShell
loads its installed System.IO.Compression assembly; no deployment dependency is added. Only an
import of zip/hash emits the respective helper; neither imports the process or move runtimes.

HTTP constants and opaque type identities use HttpValues as a shared binder/editor catalog.
Request, options, headers, query parameters and results are ordinary bound value structs.
HttpEmission recursively adapts their private field keys to ordered backend data and back;
it evaluates expressions once and keeps PowerShell enumeration at the boundary. The C# 5
Http helper uses installed System.Net.Http, bounded detached immutable Body bytes, reusable
clients, explicit proxy/credentials, a shared transfer deadline and response disposal.
Redirect policy is implemented independently of handler defaults. Successful downloads stream
to a sibling stage and publish without replacement; non-200 bodies use the bounded memory path.
The canonical strict encoding selector handles request encoding and response decoding. Only
an http import emits this helper; it does not load process containment or file-move runtimes.
