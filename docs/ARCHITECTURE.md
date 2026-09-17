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
Module loading and name resolution
  ↓
Type checking
  ↓
PowerShell generation
```

Each stage has a separate responsibility:

- Lexer splits strings, comments, and operators into tokens.
- Parser builds the syntax tree without type checking or name resolution.
- ModuleLoader resolves imports and checks dependency order and cycles.
- TypeChecker validates names, scopes, redeclarations, types, implicit numeric widening, nullable flow narrowing, function calls, and control-flow semantics.
- Emitter consumes validated syntax trees and type information to generate standalone Windows PowerShell 5.1-compatible `.ps1` files. It does not infer semantics or resolve names.
- Compiler invokes these stages in order and validates the generated PowerShell syntax.
- `rukbat check` stops after ModuleLoader and TypeChecker. It invokes neither Emitter nor the PowerShell parser.
- Formatter validates syntax, then normalizes tokens while preserving comments. It does not check types or resolve imports.

Generated scripts include small PowerShell helpers implementing Rukbat value copies, array bounds, error propagation, and PowerShell boundaries. Only imported standard-library runtime sections are included. Deployment requires neither the Rukbat compiler nor a separate Rukbat runtime.

The entry wrapper captures working and script directories before module initialization. Standard-library runtime closures retain this path context and resolve paths explicitly; they do not depend on later `$PWD` or `Environment.CurrentDirectory` values. Emitter derives runtime source labels relative to the entry source directory while leaving compiler diagnostic paths unchanged.
