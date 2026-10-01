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
- TypeChecker validates type compatibility, implicit numeric widening, nullable flow narrowing, function call signatures, and control-flow return semantics.
- Emitter consumes validated syntax trees and type information to generate standalone Windows PowerShell 5.1-compatible `.ps1` files. It does not infer semantics or resolve names.
- Compiler invokes these stages in order. It validates the generated PowerShell syntax as a generator sanity check; Windows PowerShell 5.1 compatibility is established by the execution test suite, not by this parser validation alone.
- `rukbat check` stops after ModuleLoader, Resolver, and TypeChecker. It invokes neither Emitter nor the PowerShell parser.
- Formatter validates syntax, then normalizes tokens while preserving comments. It does not check types or resolve imports.
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

The entry wrapper captures working and script directories before module initialization. Standard-library runtime closures retain this path context and resolve paths explicitly; they do not depend on later `$PWD` or `Environment.CurrentDirectory` values. Emitter derives runtime source labels relative to the entry source directory while leaving compiler diagnostic paths unchanged.
