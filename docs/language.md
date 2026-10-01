# Rukbat Language Specification

## Confirmed specification

Rukbat is a typed, procedural language for Windows system scripting. It is not PowerShell syntax. Successful compilation produces one standalone Windows PowerShell 5.1-compatible `.ps1` file.

## Commands and arguments

`rukbat build input.rukbat` writes `input.ps1`. `rukbat run input.rukbat -- arguments...` builds and executes it with Windows PowerShell. `rukbat check input.rukbat` runs module loading, name resolution, and type checking without generating PowerShell. `rukbat fmt input.rukbat` rewrites one syntactically valid source file into the canonical layout without resolving its imports or type-checking it. `rukbat fmt directory` recursively formats `.rukbat` files under that directory; `rukbat fmt .` selects the current directory. Directory traversal excludes `.git`, `bin`, `obj`, and symbolic links/junctions, and recognizes the `.rukbat` extension case-insensitively. An empty directory succeeds. Source syntax errors leave the affected file unchanged; syntax and per-file I/O errors are reported while processing continues with other files. Successfully formatted files remain changed even when another file fails. `build`, `check`, and `fmt` do not accept program arguments. Compilation and formatting syntax errors exit with code 1; command-line and I/O failures exit with code 2, taking priority when directory formatting encounters both kinds of error. Compilation failure leaves an existing output unchanged. If compilation succeeds and the generated program starts, `rukbat run` exits with the program's exit code. If the PowerShell process fails to launch, `rukbat run` exits with code 2.

`args(): string[]` returns the arguments supplied to the entry program. The compiler command, source path, and `--` separator are excluded.

`exit(code: int): void` terminates the entire program with the supplied code, including when called
inside a function or imported module. Codes from 0 through 2147483647 are accepted.
The argument is evaluated once. A negative code raises ordinary Error; a valid exit is a terminal
control transfer, cannot be caught, and satisfies the function's requirement to return or terminate.
The generated script must be executed in its own PowerShell process; exit terminates that host process.
`eprint(value): void` is the built-in stderr counterpart of `print(value)` and requires no import.
Both accept one positional value with the same type checks, append one platform newline, and do not contribute to function return values. Printing a diagnostic does not set the exit code.
Explicit string conversion and strict parsing use the `text` module described in STDLIB.md.

`rukbat test [path]` discovers ordinary top-level `fn test_name(): void` functions
in `test_*.rukbat` files. A test may instead take one `test.Context` parameter for
temporary filesystem fixtures. Testing adds no syntax or general function-value
feature. Assertions are standard-library operations (`import test`). The
[testing guide](TESTING.md) defines discovery, filtering, deadlines, isolation,
and exit codes.

## Declarations and assignment

```rukbat
name := "Taro"
age: int = 30
age = 31

fn greet(person: string): void {
    print("Hello, " + person)
}
```

- `name := value` declares a new variable and infers its type.
- Inference uses the initializer expression's resolved static type unchanged, including a struct's defining module and any array or nullable wrappers. This applies equally to local functions, imported functions, and standard-library calls such as `result := process.run(...)`; no type name is re-resolved in the caller's module.
- `name: Type = value` declares a new variable with an explicit type.
- `name = value` assigns to an existing variable.
- A declaration requires an initial value. `:=` requires an inferable type; `items := []` is invalid.
- Redeclaring a name in the same scope is invalid. `:=` never means assignment.
- `fn` is the only function declaration syntax. Free functions are defined at module scope. Methods are defined inside a struct declaration.

Names are case-sensitive. Statements are separated by a newline or `;`. Blocks use braces.
Line comments begin with `//` and continue to the end of the line. Block comments are not supported.
Comment markers inside strings are literal text. Outside strings and `//` comments, `#` is an invalid character.

## Scope and execution

Each module executes its top-level statements once, after its dependencies have initialized. Functions are available regardless of declaration order and capture the variables of their defining module. A block creates a child scope: it may shadow an outer variable, but a name cannot be redeclared in the same scope.

`return` is valid only inside a function. A non-`void` function must return a value or throw on every reachable path. A `void` function may omit its final `return`. Expressions and function arguments are evaluated from left to right; `&&` and `||` short-circuit.

Structs and arrays are mutable value types. Their stored values can be updated, but assignment, argument passing, return, and placement into another value copy them recursively. Mutating a copied value never changes the storage location from which it was copied.

## Types, nullable values, and conversions

The primitive types are `int`, `float`, `string`, and `bool`. `int` is a signed 32-bit integer. `float` is a finite IEEE 754 binary64 value. `void` is return-only. Rukbat also has user-defined `struct` types, typed arrays `T[]`, and nullable types `T?`.

`import json` provides the opaque, immutable type `json.Value` for JSON documents with an unknown
structure. It uses ordinary variable, parameter, array, struct-field, and return syntax. It has no
public fields, struct literal, or general dynamic member/index access. Read, update, and convert
its contents with the operations in STDLIB.md. A `json.Value` representing JSON `null` is a value,
distinct from a Rukbat `null`; the latter requires `json.Value?`. JSON updates return new values
and require explicit assignment. Original documents and previously selected children are unchanged.

`import csv` provides `csv.Table` with `columns: string[]` and `rows: csv.Row[]`. `csv.Row` is an
opaque, immutable row accessed by column name through standard-library functions. Tables use
ordinary struct and array copy semantics; row updates return new rows. These types do not add
dynamic indexing or new syntax. See STDLIB.md for header validation and serialization.

```rukbat
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

Equality and direct `print` / `eprint` are not defined for `json.Value` or `csv.Row`. Use `json.as<T>` to read a
primitive or `json.stringify` to obtain JSON text.

## Errors

Runtime errors report source names relative to the entry source directory, normalized with `/` separators, and the original line number. Imports outside that directory use `../` segments. Generated error metadata does not embed absolute development source paths and does not require source files at runtime. Compile-time diagnostics still use the actual source paths. User-written string literals, such as explicit absolute data paths, are preserved rather than redacted.

`Error` is a built-in value type with a `message: string` field. `throw` accepts an `Error`; `try` has one untyped `catch` binding and there is no `finally`. PowerShell failures, invalid external values, arithmetic failures, and array-bound failures enter the same Error path. Rethrowing an Error preserves its original source location. `Error` is constructed with the same struct literal syntax: `throw Error { message: "invalid configuration" }`. It is otherwise an ordinary value type.

## Types and expressions

Arrays have mutable lengths. `values.append(value)` adds one value to the end of an array and returns `void`. `append` exists only on an array value that is assignable as the left side of `=`: a variable, or a chain of struct fields and array indexes rooted at a variable. It cannot mutate a temporary value such as a function result. The appended value must be compatible with the array element type.

Every index expression in an assignment or `append` receiver is evaluated exactly once, from left to right. A nested update writes the result back to the addressed storage location rather than mutating a temporary copy.

- Integer division truncates toward zero.
- `+` concatenates two strings.
- Equality follows the rules defined in "Numbers and equality" above.
- Boolean `&&` and `||` short-circuit.
- Array literals use `[value, ...]`; arrays can grow with the statement `values.append(value)`.
- An array literal with a contextual type (`name: T[] = [...]` or parameter of type `T[]`) converts each element to `T` using the standard implicit conversions. Without a contextual type, all elements must have the same type after `int`-to-`float` promotion; `[1, 2.0]` infers `float[]`. A literal mixing incompatible types is a compile-time error. An explicitly typed empty array (`items: string[] = []`) is valid.
- `items[index]` reads or writes an element, and `items.length` is read-only.

## Control flow

`if` / `else`, `for item in items`, `while condition`, `break`, `continue`, and `return` are explicit control-flow constructs. A `while` condition must be `bool` and is evaluated before each iteration. `break` exits the innermost `for` or `while`; `continue` starts its next iteration. A loop alone does not prove a return because it may execute zero times.

The iterable expression of a `for` loop is evaluated exactly once when the loop begins. The loop iterates over a snapshot of that resulting array value. Modifying the original array variable inside the loop body (including `append`) does not affect the iteration sequence. The loop variable is a copy of each element; assigning to it does not modify the source array.

## Structs and modules

```rukbat
struct User {
    name: string
    age: int

    fn is_adult(): bool {
        return self.age >= 20
    }
}

user := User { name: "Taro", age: 30 }
```

Struct fields are data. A struct value is created with a struct literal: `StructName { field: value, ... }`. All fields must be initialized, including nullable fields. Fields are specified by name; order does not matter. Duplicate, missing, or unknown field names are compile-time errors. Field initializer expressions are evaluated left to right in written order.

Methods use `fn` and may read or update `self`. A method that updates `self` may only be called on a receiver that is assignable: a variable, or a chain of struct fields and array indexes rooted at a variable. Calling a mutating method on a temporary value, such as a function result, is a compile-time error. This matches the existing rule for `append`.

Imports are static and resolved before execution. Relative project modules use an explicit relative `.rukbat` path and an alias. Standard-library modules use their canonical module names as defined in `STDLIB.md`.

Filesystem operations use `import fs`; path-value operations use `import path`.

## PowerShell boundary

The generated script captures its initial filesystem working directory from PowerShell's location and its script directory from `$PSScriptRoot` before initializing modules. Relative standard-library file paths and child process working directories always use the captured working directory, even if escape-hatch code changes PowerShell's location or .NET's current directory. Escape-hatch commands themselves retain PowerShell semantics.

Local imports remain compile-time paths relative to the importing source file. They are unrelated to runtime working and script directories. `path.script_dir()` explicitly locates data beside the deployed `.ps1`. Execute the generated artifact as a file in a separate PowerShell process; source mapping is error reporting, not a `rukbat debug` command.

`powershell<T>(command, ...)` is the explicit escape hatch for a PowerShell operation not covered by Rukbat. It checks the returned value against `T`; a scalar result requires exactly one value and `T[]` collects values into an array. PowerShell failures and incompatible results are runtime errors.

PowerShell arguments and result types cannot contain `json.Value` or `csv.Row`. Request a string and use
`json.parse` to read a JSON document from an external command.

No output is distinct from one explicit `$null` output. No output is an Error for `T` and `T?`, while an explicit `$null` is accepted only by `T?`. No output becomes `[]` for `T[]`; one `$null` becomes `[null]` for `T?[]` and is an Error for `T[]`. Nullable arrays `T[]?` accept one explicit `$null`, but no output remains an Error. External float values must be numeric and finite.

Generated scripts do not require the Rukbat compiler or a PowerShell 7 installation. PowerShell is the target implementation, not part of Rukbat source syntax or standard-library semantics.

Execution requires FullLanguage mode. Generated scripts check this before user operations and exit nonzero with a diagnostic in restricted modes. They do not relax execution policy. Additional operating-system and filesystem requirements for contained processes and recoverable file moves are specified in `STDLIB.md`.

## Formatting

Formatting is canonical and has no configuration. `rukbat fmt` uses four-space indentation, one statement per line, spaces around binary operators and after commas/colons, no padding immediately inside delimiters, braces on the declaration or control-flow line, and `else` / `catch` on the same line as the preceding closing brace. It preserves comments and blank lines between statements or comments, collapsing consecutive blank lines to one. Leading blank lines and blank lines immediately inside block braces are removed. It writes UTF-8 without a BOM and LF line endings, and always ends with one blank line: nonempty content ends with `\n\n`, while an empty source becomes `\n`. Tree-sitter grammar, syntax highlighting, and LSP are not implemented.

The formatter also inserts one blank line between an import group and other statements, and before and after functions, struct declarations, and control-flow statements (`if`, `for`, `while`, and `try`) when another statement exists in the same block. Struct fields and methods are separated in the same way. Consecutive simple statements remain adjacent unless the source explicitly separates them. No blank line is inserted at a block's edges or between an `if` and its `else`, or a `try` and its `catch`. Leading comments stay with the following statement; trailing comments stay with the preceding statement.

Struct literals use indented braces; their closing brace stays attached to any following comma, member access, index, or closing expression delimiter. Expression continuations normally join onto one line. When a line comment requires a continuation to remain on a new line, it receives one additional four-space indent. Closing delimiters align with the enclosing block. Comments can also require an opening brace, `else`, or `catch` to remain on a separate line. Physical CR and LF characters inside string literals become `\r` and `\n` escapes, preserving the string value while keeping source line endings canonical. Formatting twice produces the same result as formatting once.

## Current exclusions

Rukbat does not provide union types, arbitrary object types, pipelines, closures, function values, package management, multiple catch clauses, `finally`, or generic language syntax. Standard-library API design is specified separately and must not be inferred from the current compiler layout.

## Not yet specified

The following are not confirmed language specification: function overloads, a named `main` entry point, explicit numeric conversion APIs, and integer-overflow policy beyond current checked runtime behavior.
