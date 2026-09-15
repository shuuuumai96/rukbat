# Rukbat Language Specification

## Confirmed specification

Rukbat is a typed, procedural language for Windows system scripting. It is not PowerShell syntax. Successful compilation produces one standalone Windows PowerShell 5.1-compatible `.ps1` file.

## Commands and arguments

`rukbat build input.rukbat` writes `input.ps1`. `rukbat run input.rukbat -- arguments...` builds and executes it with Windows PowerShell. `rukbat check input.rukbat` runs module loading, name resolution, and type checking without generating PowerShell. `rukbat fmt input.rukbat` rewrites one syntactically valid source file into the canonical layout without resolving its imports or type-checking it. `build`, `check`, and `fmt` do not accept program arguments. A compilation or formatting failure exits with code 1 without changing an existing output or source file; command-line and I/O failures exit with code 2.

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

## Scope and execution

Each module executes its top-level statements once, after its dependencies have initialized. Functions are available regardless of declaration order and capture the variables of their defining module. A block creates a child scope: it may shadow an outer variable, but a name cannot be redeclared in the same scope.

`return` is valid only inside a function. A non-`void` function must return a value or throw on every reachable path. A `void` function may omit its final `return`. Expressions and function arguments are evaluated from left to right; `&&` and `||` short-circuit.

## Types, nullable values, and conversions

The primitive types are `int`, `float`, `string`, and `bool`. `int` is a signed 32-bit integer. `float` is a finite IEEE 754 binary64 value. `void` is return-only. Rukbat also has user-defined `struct` types, typed arrays `T[]`, and nullable types `T?`.

```auv
nickname: string? = null
values: string?[] = ["one", null]
optional_values: string[]? = null
```

`T` never contains `null`; `T?` contains either a `T` value or `null`. `T` and `null` convert to `T?`. `T?` does not implicitly convert to `T`, `T??` and `void?` are invalid, and `x := null` is invalid because it has no inferable concrete type. Array element types are invariant: `string[]` converts to `string[]?`, but not to `string?[]`, and `int[]` does not convert to `float[]`.

`int` converts implicitly to `float`, including the explicit nullable combinations `int` to `float?` and `int?` to `float?`. There are no other implicit numeric conversions.

A nullable variable or parameter can be narrowed by a direct `== null` or `!= null` check. An early `return` or `throw` from the null branch also narrows the following path. Assignment recalculates the variable's narrowed state. Struct fields, array elements, and other complex expressions are not narrowed in v0.1.

Module variables can also be narrowed. A call to an ordinary function in the same module invalidates their narrowing because that function may modify them. `print`, standard built-ins, and functions in another module do not invalidate current-module narrowing.

Before narrowing, a `T?` value can only be compared directly with the `null` literal, stored in a compatible nullable location, passed or returned as a nullable value, encoded as JSON, or passed through a nullable-aware external boundary. General equality between two nullable expressions is not defined in v0.1.

## Numbers and equality

Float literals include `1.0`, `1e3`, `1E3`, and `1.5e-2`. The abbreviated forms `.5` and `1.` are invalid. Mixed `int`/`float` arithmetic and ordering convert the integer operand to `float`; `%` is defined only for `int`.

Integer or float division by zero is a runtime Error. A float literal, arithmetic result, or external value that is NaN or positive or negative Infinity is rejected. Other float operations use binary64 behavior.

Primitive equality requires compatible non-null values and string equality is case-sensitive. Mixed `int`/`float` equality uses the `int` to `float` conversion. Struct and array equality are not defined. A nullable value may be compared directly with `null`.

## Errors

`Error` is a built-in value type with a `message: string` field. `throw` accepts an `Error`; `try` has one untyped `catch` binding and there is no `finally`. PowerShell failures, invalid external values, arithmetic failures, and array-bound failures enter the same Error path. Rethrowing an Error preserves its original source location.

## Not yet specified / TBD

The following are not confirmed language specification: function overloads, a named `main` entry point, explicit numeric conversion APIs, and integer-overflow policy beyond current checked runtime behavior.

Formatting is canonical and has no configuration. `rukbat fmt` uses four-space indentation, one statement per line, spaces around binary operators and after commas/colons, no padding immediately inside delimiters, braces on the declaration or control-flow line, and `else` / `catch` on the same line as the preceding closing brace. It preserves comments, writes UTF-8 without a BOM, and normalizes a nonempty file to LF line endings with one final newline. Tree-sitter grammar, syntax highlighting, and LSP are not implemented.

## Additional language behavior

## Types and expressions

Arrays are fixed length. Structs and arrays currently use recursive value-copy behavior for assignment, arguments, and returns.

- Integer division truncates toward zero.
- `+` concatenates two strings.
- Equality compares values of the same primitive type and string equality is case-sensitive.
- Boolean `&&` and `||` short-circuit.
- Array literals use `[value, ...]`; arrays are fixed length.
- `items[index]` reads or writes an element, and `items.length` is read-only.

## Control flow

`if` / `else`, `for item in items`, `break`, `continue`, and `return` are explicit control-flow constructs. A loop alone does not prove a return because it may execute zero times.

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

Struct fields are data. Methods use `fn` and may update `self`. Imports are static and resolved before execution. Relative project modules use an explicit relative `.rukbat` path and an alias. Standard-library modules use their canonical module names as defined in `STDLIB.md`.

## PowerShell boundary

`powershell<T>(command, ...)` is the explicit escape hatch for a PowerShell operation not covered by Rukbat. It checks the returned value against `T`; a scalar result requires exactly one value and `T[]` collects values into an array. PowerShell failures and incompatible results are runtime errors.

No output is distinct from one explicit `$null` output. No output is an Error for `T` and `T?`, while an explicit `$null` is accepted only by `T?`. No output becomes `[]` for `T[]`; one `$null` becomes `[null]` for `T?[]` and is an Error for `T[]`. Nullable arrays `T[]?` accept one explicit `$null`, but no output remains an Error. External float values must be numeric and finite.

Generated scripts do not require the Rukbat compiler or a PowerShell 7 installation. PowerShell is the target implementation, not part of Rukbat source syntax or standard-library semantics.

## Current exclusions

Rukbat does not provide union types, arbitrary object types, pipelines, closures, function values, package management, multiple catch clauses, `finally`, or generic language syntax. Standard-library API design is specified separately and must not be inferred from the current compiler layout.
