# Rukbat Language Specification

Rukbat is a typed, procedural language for Windows system scripting. It is not PowerShell syntax. Successful compilation produces one standalone Windows PowerShell 5.1-compatible `.ps1` file.

## Commands and arguments

| Command | Behavior |
|---|---|
| `rukbat --version` | Print the compiler version without starting PowerShell. |
| `rukbat doctor` | Probe local PowerShell/native-helper capabilities without changing policy; see [distribution](DISTRIBUTION.md). |
| `rukbat build input.rukbat` | Validate and write `input.ps1`. |
| `rukbat run input.rukbat -- arguments...` | Build, then execute with Windows PowerShell. |
| `rukbat check input.rukbat` | Check imports, names, and types without generation. |
| `rukbat fmt input.rukbat` / `rukbat fmt directory` | Apply [canonical formatting](#formatting) to a file or directory. |
| `rukbat test [path]` | Discover and run tests; see [TESTING.md](TESTING.md). |
| `rukbat doc input.rukbat` | Generate source documentation; see [DOCUMENTATION.md](DOCUMENTATION.md). |
| `rukbat lsp` | Serve the language protocol; see [EDITOR.md](EDITOR.md#language-server-protocol). |

`--version` and `doctor` reject extra arguments. `build`, `check`, and `fmt` do not
accept program arguments. Compilation failures leave existing output unchanged.
Compiler success exits 0, compilation/formatting syntax errors exit 1, and argument
or I/O errors exit 2. `doctor` exits 0 on success or 2 on a failed probe/capability.
After launch, `run` returns the program's exit code; a launch failure exits 2.

`args(): string[]` returns the arguments supplied to the entry program. The compiler command, source path, and `--` separator are excluded.
`import cli` provides explicit definitions, parsing and help for program arguments without new
syntax or automatic output/exit. Its APIs are defined in STDLIB.md and illustrated in [CLI.md](CLI.md).

`exit(code: int): void` terminates the entire program with the supplied code, including when called
inside a function or imported module. Codes from 0 through 2147483647 are accepted.
The argument is evaluated once. A negative code raises ordinary Error; a valid exit is a terminal
control transfer, cannot be caught, and satisfies the function's requirement to return or terminate.
The generated script must be executed in its own PowerShell process; exit terminates that host process.
`eprint(value): void` is the built-in stderr counterpart of `print(value)` and requires no import.
Both accept one positional value with the same type checks, append one platform newline, and do not contribute to function return values. Printing a diagnostic does not set the exit code.
Explicit string conversion and strict parsing use the `text` module described in STDLIB.md.

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
- Prefix a variable declaration with `const` to prohibit writes through that name:
  `const max_retries := 3` or `const timeout_ms: int = 5000`. Initialization is
  required and follows ordinary runtime evaluation, source order, and type rules.
  Initializers may call functions, including `args()`. Each declaration evaluates
  its initializer once when execution reaches it, including on each loop iteration
  or function invocation. `const` does not add compile-time expression evaluation.
- Const declarations are allowed at module level and in ordinary statement blocks.
  They cannot qualify parameters, struct fields, or function declarations. `const`
  is a reserved word.
- Assignment to a const name, any field/index path rooted at that name, array
  `append`, dictionary `remove`, and a method that modifies that receiver produce `RUK2011`.
  This includes nested arrays/structs/dictionaries and narrowed nullable values. Reading and
  non-mutating method calls are allowed. Method mutation is determined after
  binding: writes to `self` or its fields/indexes, array append or dictionary removal through `self`, and
  indirect calls to mutating methods through `self` paths are propagated, including
  recursion and imported methods. All possible body paths count, including writes
  in unreachable branches; runtime reachability is not inferred.
- Ordinary assignment, arguments, returns, and loop bindings still copy values.
  A copy of a const value held by an ordinary variable or parameter is mutable.
  `const` is a restriction on a declared name, not a new type. Const module values
  remain module-local, like ordinary variables.
- `fn` is the only function declaration syntax. Free functions are defined at module scope. Methods are defined inside an `impl` declaration, separately from the struct's fields.

Names are case-sensitive. Statements are separated by a newline or `;`. Blocks use braces.
Line comments begin with `//` and continue to the end of the line. Block comments are not supported.
Comment markers inside strings are literal text. Outside strings and `//` comments, `#` is an invalid character.

`//!` and `///` are documentation comments; `////` remains an ordinary comment.
They do not change compilation semantics. Placement/link errors do not make
`rukbat check` fail. See [DOCUMENTATION.md](DOCUMENTATION.md) for attachment,
Markdown, validation, and formatter preservation rules.

## Scope and execution

Each module executes its top-level statements once, after its dependencies have initialized. Functions are available regardless of declaration order and capture the variables of their defining module. A block creates a child scope: it may shadow an outer variable, but a name cannot be redeclared in the same scope.

`return` is valid only inside a function. A non-`void` function must return a value or throw on every reachable path. A `void` function may omit its final `return`. Expressions and function arguments are evaluated from left to right; `&&` and `||` short-circuit.

Structs, arrays, and dictionaries are mutable value types. Their stored values can be updated, but assignment, argument passing, return, and placement into another value copy them recursively. Mutating a copied value never changes the storage location from which it was copied.

## Types, nullable values, and conversions

The primitive types are `int`, `float`, `string`, and `bool`. `int` is a signed 64-bit integer. `float` is a finite IEEE 754 binary64 value. `void` is return-only. Rukbat also has user-defined `struct` types, typed arrays `T[]`, typed dictionaries `dict[K, V]`, and nullable types `T?`.

Standard modules also define ordinary structs and opaque immutable types. Opaque
types use ordinary variable, parameter, return, field, array, dictionary, and nullable
syntax, but expose no public fields or struct literals. Their APIs and conversion
rules are defined in [STDLIB.md](STDLIB.md).

| Type | Language behavior |
|---|---|
| `json.Value` | Unknown JSON structure; no dynamic member/index access, equality, or direct printing. JSON null is a value distinct from Rukbat null. Updates return new values. |
| `csv.Row` | Immutable row accessed/updated through library functions. No equality or direct printing. `csv.Table` is an ordinary struct with `columns: string[]` and `rows: csv.Row[]`. |
| `encoding.Encoding`, `hash.Algorithm` | Validated read-only constants and same-type equality; no ordering or implicit string/numeric conversion. External names use explicit parse/name operations. |
| `log.Level` | Validated severity constants and non-null same-type equality; no ordering, arithmetic, fields, struct literal, or implicit conversion. `log.Logger` is an ordinary struct with copyable configuration fields. |
| `http.Body`, `http.Method` | Immutable response bytes and validated method constants. Only Method has equality. HTTP request/options/result types are ordinary structs; status constants are read-only ints. |
| `fs.Lines` | Immutable file-line descriptor; for owns each independent traversal. No equality, direct printing, or exposed fields. |
| `re.Pattern` | Validated immutable regular expression with an explicit match timeout. No equality, direct printing, or exposed fields. `re.Match` is an ordinary struct with value, code-point range, and nullable capture strings. |
| `datetime.Timestamp`, `datetime.Date`, `datetime.Duration`, `datetime.Zone` | Explicit conversions/arithmetic; same-type equality, and ordering except for Zone. Named zones are read-only constants. |

Encoding, hash, log.Level, HTTP opaque types, fs.Lines, re.Pattern, and datetime types are recursively rejected at
printing, JSON, and PowerShell boundaries. JSON/CSV updates preserve originals; ordinary container copies remain
independent. ZIP exports ordinary `zip.Options` and `zip.Entry` structs.

```rukbat
nickname: string? = null
values: string?[] = ["one", null]
optional_values: string[]? = null
```

`T` never contains `null`; `T?` contains either a `T` value or `null`. `T` and `null` convert to `T?`. `T?` does not implicitly convert to `T`, `T??` and `void?` are invalid, and `x := null` is invalid because it has no inferable concrete type. Array element types are invariant: `string[]` converts to `string[]?`, but not to `string?[]`, and `int[]` does not convert to `float[]`.

`int` and `float` have no implicit conversions, including nullable values. Use the explicit `math` conversion operations in STDLIB.md.

A nullable variable or parameter can be narrowed by a direct `== null` or `!= null` check. An early `return` or `throw` from the null branch also narrows the following path. Assignment recalculates the variable's narrowed state. Struct fields, array elements, and other complex expressions are not narrowed.

Module variables can also be narrowed. A call to an ordinary function in the same module invalidates their narrowing because that function may modify them. `print`, standard built-ins, and functions in another module do not invalidate current-module narrowing.

Before narrowing, a `T?` value can only be compared directly with the `null` literal, stored in a compatible nullable location, passed or returned as a nullable value, encoded as JSON when its type supports JSON, or passed through a supported nullable-aware external boundary. General equality between two nullable expressions is not defined.

## Numbers and equality

Float literals include `1.0`, `1e3`, `1E3`, and `1.5e-2`. The abbreviated forms `.5` and `1.` are invalid. Arithmetic and comparison operands must have the same numeric type; `%` is defined only for `int`.

Integer or float division by zero is a runtime Error. A float literal, arithmetic result, or external value that is NaN or positive or negative Infinity is rejected. Integer arithmetic is checked: range overflow raises Error without wrapping or promotion. Division truncates toward zero; the remainder has the dividend's sign. The minimum integer divided by -1 raises Error, while its remainder is zero. The supported integer range is -9223372036854775808 through 9223372036854775807. Other float operations use binary64 round-to-nearest, ties-to-even behavior; subnormal values and arithmetic underflow to zero are allowed. Positive and negative float zero compare equal.

Primitive equality requires compatible non-null values and string equality is case-sensitive. Mixed `int`/`float` equality is rejected. Struct, array, and dictionary equality are not defined. A nullable value may be compared directly with `null`.

## Errors

Runtime errors report source names relative to the entry source directory, normalized with `/` separators, and the original line number. Imports outside that directory use `../` segments. Generated error metadata does not embed absolute development source paths and does not require source files at runtime. Compile-time diagnostics still use the actual source paths. User-written string literals, such as explicit absolute data paths, are preserved rather than redacted.

`Error` is a built-in value type with a `message: string` field. `throw` accepts an `Error`; `try` has one untyped `catch` binding and there is no `finally`. PowerShell failures, invalid external values, arithmetic failures, and array-bound failures enter the same Error path. Rethrowing an Error preserves its original source location. `Error` is constructed with the same struct literal syntax: `throw Error { message: "invalid configuration" }`. It is otherwise an ordinary value type.

## Types and expressions

`import array` provides slice, removal, and stable sort operations that return new arrays.
Element types are inferred from typed array arguments.
See STDLIB.md for range checks, supported key types and ordering rules.

Arrays have mutable lengths. `values.append(value)` adds one value to the end of an array and returns `void`. `append` exists only on an array value that is assignable as the left side of `=`: a variable, or a chain of struct fields and array indexes rooted at a variable. It cannot mutate a temporary value such as a function result. The appended value must be compatible with the array element type.

Every index expression in an assignment or `append` receiver is evaluated exactly once, from left to right. A nested update writes the result back to the addressed storage location rather than mutating a temporary copy.

- `+` concatenates two strings.
- Array literals use `[value, ...]`; arrays can grow with the statement `values.append(value)`.
- An array literal with a contextual type (`name: T[] = [...]` or parameter of type `T[]`) converts each element to `T` using the standard implicit conversions. Without a contextual type, all elements must have the same type; `[1, 2.0]` is rejected. A literal mixing incompatible types is a compile-time error. An explicitly typed empty array (`items: string[] = []`) is valid.
- `items[index]` reads or writes an element, and `items.length` is read-only.

## Dictionaries

`dict[K, V]` stores dynamically keyed values. K is exactly non-null `string` or `int`.
V can be any storable type, including nullable values, structs, arrays, dictionaries, and
opaque standard values. `void` and recursive struct cycles through dictionary values are
rejected. K and V are invariant. `dict` is a dedicated built-in type constructor, not a
module or general generic facility. A struct cannot be named `dict`; an otherwise legal
variable/import alias can. No import is needed.

```rukbat
counts: dict[string, int] = {}
counts["error"] = 1
if counts.has("error") {
    counts["error"] = counts["error"] + 1
}
for key in counts.keys() {
    print(key)
    print(counts[key])
}
counts.remove("error")
print(counts.length)
```

Literals use `{key: value, ...}`, with expression keys and optional trailing commas.
`ports := {"http": 80, "https": 443}` infers `dict[string, int]`; an identifier key uses
that variable's value. Without context, the first pair determines exact K/V and all later
pairs must match. `{}` and null values require a contextual dictionary type from a declaration,
argument, return, field, or enclosing literal. Struct literals retain their explicit type name.

| Operation | Result and behavior |
|---|---|
| `d[key]` | V; an absent key raises Error, even if V is nullable. |
| `d[key] = value` | Insert or replace in writable storage. |
| `d.has(key)` | bool; true for a stored null. |
| `d.remove(key)` | void; remove in writable storage, absent keys succeed. |
| `d.keys()` | Independent K[] snapshot in insertion order. |
| `d.length` | int entry count, read-only. |

Methods take exactly the positional arguments shown. String keys use ordinal, case-sensitive
equality without normalization, trimming, or path comparison. Empty strings and the entire
signed 64-bit int range are valid. Replacing a value preserves the key's position.
Removing and reinserting a key moves it to the end. This ordering is the same in
PowerShell 5.1 and 7.

Repeated string or signed-integer literal keys are compile-time errors. Other duplicates raise
Error during construction. Each pair evaluates key then value in written order before the
duplicate check; earlier effects are not rolled back. Indexed assignment replaces existing keys.
`:=` cannot declare an indexed target.

Ordinary value copies are recursive. `user := users[id]` copies a stored struct;
`users[id].name = "Taro"` updates stored fields. Writable paths include dictionary indexes
rooted at a variable. Const roots reject insertion, replacement, removal, and nested mutation,
including mutation forwarded through struct methods. Mutating temporary function results is
rejected. Intermediate keys must exist; only terminal dictionary-index assignment inserts.
There is no automatic creation of nested values. Key/index expressions evaluate once, left to right.

Iteration uses `for key in d.keys()`. Mutations do not change that snapshot; `d[key]` still
reads current contents and can fail after removal. Copy d first for stable contents, or use
`array.sort(d.keys())` for sorted traversal. Dictionary equality and direct print/eprint are
not defined. `has` does not narrow nullable stored values: read into a variable and check
against null. Nullable dictionary variables use existing narrowing rules.

## Control flow

`if` / `else`, `for item in items`, `while condition`, `break`, `continue`, and `return` are explicit control-flow constructs. A `while` condition must be `bool` and is evaluated before each iteration. `break` exits the innermost `for` or `while`; `continue` starts its next iteration. A loop alone does not prove a return because it may execute zero times.

The iterable expression of a `for` loop is evaluated exactly once when the loop begins. For an array, the loop iterates over a snapshot of that value. Modifying the original array variable inside the loop body (including `append`) does not affect the iteration sequence. The loop variable is a copy of each element; assigning to it does not modify the source array.

A `for` also accepts non-null `fs.Lines`, yielding strings. Each loop owns an independent
file traversal and closes it on EOF, break, return, or escaping Error. Continue keeps it open.
Return expressions are evaluated and copied before closure; return values are emitted after
successful cleanup. Cleanup failure follows the ordinary Error path. A caught primary Error
retains its location when cleanup also fails. Valid exit and forced termination do not guarantee
finally execution. Descriptor, decoding, and read-ahead behavior are defined in STDLIB.md.

## Structs and modules

```rukbat
struct User {
    name: string
    age: int
}

impl User {
    fn is_adult(): bool {
        return self.age >= 20
    }
}

user := User { name: "Taro", age: 30 }
print(user.is_adult())
```

A `struct` declaration defines data using fields only. Methods inside a `struct` are rejected; define them in `impl StructName { ... }` instead. A struct value is created with a struct literal: `StructName { field: value, ... }`. All fields must be initialized, including nullable fields. Fields are specified by name; order does not matter. Duplicate, missing, or unknown field names are compile-time errors. Field initializer expressions are evaluated left to right in written order.

An `impl` declaration contains only `fn` methods and must be at module scope. Its target is the unqualified name of a user-defined struct in the same module. It may appear before or after that struct; at most one `impl` is allowed for each struct. Imported types, built-in types, arrays, and dictionaries cannot be extended with `impl`. `impl` is a reserved word. Method names must be unique and cannot conflict with the struct's fields.

Methods use `fn` and may read or update `self`. A method that updates `self` may only be called on a receiver that is assignable: a variable, or a chain of struct fields and array indexes rooted at a variable. Calling a mutating method on a temporary value, such as a function result, is a compile-time error. This matches the existing rule for `append`.

Imports are static and resolved before execution. Relative project modules use an explicit relative `.rukbat` path and an alias. Standard-library modules use their canonical module names as defined in `STDLIB.md`.

## PowerShell boundary

The generated script captures its initial filesystem working directory and its script directory from `$PSScriptRoot` before initializing modules. For direct `powershell -File` / `pwsh -File` execution, including `rukbat run`, the working directory is the host process's current directory, with path characters such as `[` and `]` treated literally. When a PowerShell statement invokes the script, the working directory is that session's filesystem location instead. Relative standard-library file paths use that captured directory, even if escape-hatch code changes PowerShell's location or .NET's current directory. process.Options.working_dir explicitly chooses the child's working directory; a relative setting and executable path still resolve against the captured directory. Child-only environment overrides and text input are described in STDLIB.md. Escape-hatch commands retain PowerShell semantics.

Local imports remain compile-time paths relative to the importing source file. They are unrelated to runtime working and script directories. `path.script_dir()` explicitly locates data beside the deployed `.ps1`. Execute the generated artifact as a file in a separate PowerShell process. Ordinary source locations support error reporting; the VS Code debugger uses separate development metadata described in [DEBUGGING.md](DEBUGGING.md).

`path.working_dir()` exposes the captured entry working directory. `path.resolve(base_dir, value)`
selects an explicit absolute directory for path-value resolution without changing the program's
working directory. `path.relative(base_dir, target)` computes a relative path between absolute
values. These operations do not inspect filesystem entries; their Windows path contracts are
defined in STDLIB.md.

`powershell<T>(command, ...)` is the explicit escape hatch for a PowerShell operation not covered by Rukbat. It checks the returned value against `T`; a scalar result requires exactly one value and `T[]` collects values into an array. PowerShell failures and incompatible results are runtime errors.

PowerShell arguments and result types cannot contain dictionaries or opaque standard-library values such as
`json.Value`, `csv.Row`, `cli.Result`, `encoding.Encoding`, `hash.Algorithm`, `log.Level`/`log.Logger`, `re.Pattern`, datetime types or HTTP Body/Method.
Request a string and use `json.parse` to read a JSON document from an external command.
For encodings, pass `encoding.name(value)` and parse returned text with `encoding.parse`.

No output is distinct from one explicit `$null` output. No output is an Error for `T` and `T?`, while an explicit `$null` is accepted only by `T?`. No output becomes `[]` for `T[]`; one `$null` becomes `[null]` for `T?[]` and is an Error for `T[]`. Nullable arrays `T[]?` accept one explicit `$null`, but no output remains an Error. External float values must be numeric and finite.

Generated scripts do not require the Rukbat compiler or a PowerShell 7 installation. PowerShell is the target implementation, not part of Rukbat source syntax or standard-library semantics.

Execution requires FullLanguage mode. Generated scripts check this before user operations and exit nonzero with a diagnostic in restricted modes. They do not relax execution policy. Additional operating-system and filesystem requirements for contained processes and recoverable file moves are specified in `STDLIB.md`.

## Formatting

`rukbat fmt` parses without binding or type checking. Directory mode recognizes
`.rukbat` case-insensitively and excludes `.git`, `bin`, `obj`, and links/junctions.
An empty directory succeeds. Syntax/I/O failures are reported while remaining
files are processed: valid files are rewritten, invalid files remain unchanged.
If both syntax and I/O failures occur, exit code 2 takes precedence.

Formatting is canonical and has no configuration. `rukbat fmt` uses four-space indentation, one statement per line, spaces around binary operators and after commas/colons, no padding immediately inside delimiters, braces on the declaration or control-flow line, and `else` / `catch` on the same line as the preceding closing brace. It preserves comments and blank lines between statements or comments, collapsing consecutive blank lines to one. Leading blank lines and blank lines immediately inside block braces are removed. It writes UTF-8 without a BOM and LF line endings, and ends with one LF newline (`\n`) without an additional trailing blank line. An empty source becomes `\n`.

The formatter also inserts one blank line between an import group and other statements, and before and after functions, struct declarations, impl declarations, and control-flow statements (`if`, `for`, `while`, and `try`) when another statement exists in the same block. Methods within an `impl` are separated by one blank line. Consecutive struct fields and simple statements remain adjacent unless the source explicitly separates them. No blank line is inserted at a block's edges or between an `if` and its `else`, or a `try` and its `catch`. Leading comments stay with the following statement; trailing comments stay with the preceding statement.

Struct literals use indented braces; their closing brace stays attached to any following comma, member access, index, or closing expression delimiter. Expression continuations normally join onto one line. When a line comment requires a continuation to remain on a new line, it receives one additional four-space indent. Closing delimiters align with the enclosing block. Comments can also require an opening brace, `else`, or `catch` to remain on a separate line. Physical CR and LF characters inside string literals become `\r` and `\n` escapes, preserving the string value while keeping source line endings canonical. Formatting twice produces the same result as formatting once.

## Editor support

See [EDITOR.md](EDITOR.md) for language-server behavior and editing scope, and
[DEBUGGING.md](DEBUGGING.md) for debug builds and metadata. These tools add no
language syntax or standard-library APIs.

## Unsupported features

Rukbat does not provide union types, arbitrary object types, pipelines, closures, function values, function overloads, package management, multiple catch clauses, `finally`, or user-defined generics. Module-level statements are the program entry point.
