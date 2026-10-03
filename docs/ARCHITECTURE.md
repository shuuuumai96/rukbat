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
- Emitter consumes validated syntax trees and type information to generate standalone Windows PowerShell 5.1-compatible `.ps1` files. It does not infer semantics or resolve names.
- Compiler invokes these stages in order. It validates the generated PowerShell syntax as a generator sanity check; Windows PowerShell 5.1 compatibility is established by the execution test suite, not by this parser validation alone.
- `rukbat check` stops after ModuleLoader, Resolver, and TypeChecker. It invokes neither Emitter nor the PowerShell parser.
- `rukbat test` discovers source files in the CLI, uses the same front end to bind test function signatures, then emits and validates every selected entrypoint before execution. Emitter initializes the ordinary module graph, invokes one validated function with an optional bound `test.Context`, and writes a private completion marker only after that function returns. The runner captures each PowerShell host in a separate Windows process job; process exit alone does not imply test success. The same native `ProcessJob.cs.txt` is compiled into the CLI and embedded for deployed `process.run` scripts.
- Formatter validates syntax, then normalizes tokens while preserving comments. It does not check types or resolve imports.
- `rukbat lsp` runs a stdio JSON-RPC server. LanguageAnalysis uses the same ModuleLoader, Parser, and TypeChecker as the compiler, with an in-memory source reader for open documents. Optional parser block spans describe lexical scopes; bound symbol identities provide hover and definitions without rebinding names by spelling. Completion uses those scopes and the embedded canonical STDLIB.md signatures/documentation. Formatting calls the existing Formatter and returns edits. No backend or user code is executed. The VS Code client and TextMate grammar live under `editors/vscode`.
- During formatting, Parser also records syntax facts such as continuation newlines, struct-literal closing braces, unary operators, type-argument delimiters, and adjacent statement kinds within each block. FormattingSyntax identifies required group boundaries, and Formatter uses these facts instead of guessing expression roles from token spelling. Ordinary compilation does not collect these optional facts.

Generated scripts include small PowerShell helpers implementing Rukbat value copies, array bounds, error propagation, and PowerShell boundaries. Only imported standard-library runtime sections are included. Deployment requires neither the Rukbat compiler nor a separate Rukbat runtime.

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
