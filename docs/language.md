# Auvil Language Specification

## Confirmed specification

Auvil is a typed, procedural language for Windows system scripting. It is not PowerShell syntax. Successful compilation produces one standalone Windows PowerShell 5.1-compatible `.ps1` file.

## Commands and arguments

`auvil build input.auv` writes `input.ps1`. `auvil run input.auv -- arguments...` builds and executes it with Windows PowerShell. `build` does not accept program arguments. A compilation failure exits with code 1 without changing an existing output; command-line and I/O failures exit with code 2.

`args(): string[]` returns the arguments supplied to the entry program. The compiler command, source path, and `--` separator are excluded.

## Declarations and assignment

```auv
name := "Taro"
age: int = 30
age = 31

fn greet(person: string): void {
    print("Hello, " + person)
}
```

- `name := value` declares a new variable and infers its type.
- `name: Type = value` declares a new variable with an explicit type.
- `name = value` assigns to an existing variable.
- A declaration requires an initial value. `:=` requires an inferable type; `items := []` is invalid.
- Redeclaring a name in the same scope is invalid. `:=` never means assignment.
- `fn` is the only function declaration syntax. Functions are defined at module scope.

Names are case-sensitive. Statements are separated by a newline or `;`. Blocks use braces. Comments begin with `//` or `#`.

## Not yet specified / TBD

The following are not confirmed language specification. Existing behavior must not be treated as a public contract: scope and shadowing; array and struct assignment semantics; null; function overloads; main and top-level execution; the error model; equality semantics; implicit conversions; integer arithmetic details; and return omission rules.

Formatting is not yet specified. A formatter, Tree-sitter grammar, syntax highlighting, and LSP are not implemented.

## Current implementation behavior

The following describes the current implementation only. It is not confirmed language specification: the primitive types currently recognized, array operations, control-flow analysis, module initialization, and the PowerShell value boundary.

## Types and expressions

The primitive types are `int`, `string`, `bool`, and the return-only type `void`. Auvil also has user-defined `struct` types and typed arrays `T[]`. There are no implicit conversions.

- Arithmetic operators apply to `int`; division truncates toward zero.
- `+` concatenates two strings.
- Equality compares values of the same primitive type and string equality is case-sensitive.
- Boolean `&&` and `||` short-circuit.
- Array literals use `[value, ...]`; arrays are fixed length.
- `items[index]` reads or writes an element, and `items.length` is read-only.

## Control flow

`if` / `else`, `for item in items`, `break`, `continue`, and `return` are explicit control-flow constructs. A non-void function must return a value on every reachable path. A loop alone does not prove this because it may execute zero times.

## Structs and modules

```auv
struct User {
    name: string
    age: int

    fn is_adult(): bool {
        return self.age >= 20
    }
}
```

Struct fields are data. Methods use `fn` and may update `self`. Imports are static and resolved before execution. Relative project modules use an explicit relative `.auv` path and an alias. Standard-library modules use their canonical module names as defined in `STDLIB.md`.

## PowerShell boundary

`powershell<T>(command, ...)` is the explicit escape hatch for a PowerShell operation not covered by Auvil. It checks the returned value against `T`; a scalar result requires exactly one value and `T[]` collects values into an array. PowerShell failures and incompatible results are runtime errors.

Generated scripts do not require the Auvil compiler or a PowerShell 7 installation. PowerShell is the target implementation, not part of Auvil source syntax or standard-library semantics.

## Current exclusions

Auvil does not provide union types, arbitrary object types, pipelines, closures, function values, package management, multiple catch clauses, `finally`, or generic language syntax. Standard-library API design is specified separately and must not be inferred from the current compiler layout.
