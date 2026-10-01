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

Generated scripts include small PowerShell helpers implementing Rukbat value copies, array bounds, error propagation, and PowerShell boundaries. Only imported standard-library runtime sections are included. Deployment requires neither the Rukbat compiler nor a separate Rukbat runtime.

Native Windows helpers for contained process execution and journaled file moves live in `Runtime/*.cs.txt`, embedded as compiler resources and copied into the generated script. `NativeEmission` emits lazy `Add-Type` loaders; the deployment machine uses its existing PowerShell/.NET facilities. These helpers use C# syntax compatible with the Windows PowerShell 5.1 compiler. Process job assignment is part of process creation, so host termination cannot leave a child executing outside the job. Move journals are flushed before renames; locked file handles preserve identity across moves and recovery.

Generated scripts reject non-FullLanguage sessions before user operations. They do not change or bypass the machine's execution/security policy.

The entry wrapper captures working and script directories before module initialization. Standard-library runtime closures retain this path context and resolve paths explicitly; they do not depend on later `$PWD` or `Environment.CurrentDirectory` values. Emitter derives runtime source labels relative to the entry source directory while leaving compiler diagnostic paths unchanged.
