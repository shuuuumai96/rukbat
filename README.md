# Rukbat

Rukbat is a small, statically typed language that compiles to PowerShell.
It removes `$` prefixes from variables and provides conventional function calls and file-based modules.
Source files use `.rukbat`; the output is a single `.ps1` containing all Rukbat modules needed to run it.

## Syntax

`math.rukbat`:

```rukbat
fn add(a: int, b: int): int {
    return a + b
}
```

`hello.rukbat`:

```rukbat
import "./math.rukbat" as math

struct User {
    name: string
    age: int

    fn is_adult(): bool {
        return self.age >= 20
    }
}

user: User = User {
    name: "Taro",
    age: 30
}

result: int = math.add(user.age, 2)
print(user.name)
print(result)

if user.is_adult() {
    print("adult")
}

year: string = powershell<string>("Get-Date", Format: "yyyy")
print(year)
```

`powershell<string>(...)` calls a PowerShell command and checks at runtime that its result is exactly one string.
Use `powershell<void>(...)` when no return value is needed.

## Running programs

Building requires the .NET 8 SDK. Generated scripts require Windows PowerShell 5.1 or later in FullLanguage mode. Process containment requires Windows 10 / Server 2016 or later; unsupported launch conditions fail closed.
The initial dependency restore requires a network connection.

```powershell
dotnet restore --configfile NuGet.Config
dotnet run --no-restore --project src/Rukbat -- run examples/hello.rukbat
```

On Windows, you can also use the repository wrappers:

```powershell
.\rukbat run examples/import-data.rukbat
# With this directory on PATH:
rukbat run examples/import-data.rukbat
# Generate a .ps1 without running it:
.\rukbat build examples/hello.rukbat
# Check syntax, imports, and types without generating PowerShell:
.\rukbat check examples/hello.rukbat
# Apply canonical source formatting:
.\rukbat fmt examples/hello.rukbat
# Format every .rukbat source under the current directory:
.\rukbat fmt .
# A build-only shortcut is also available:
.\build examples/hello.rukbat
```

`run` executes the generated `.ps1` with `powershell -NoLogo -NoProfile -File` only after successful type checking and compilation.
It inherits the caller's working directory and standard streams and returns the PowerShell process exit code.
Compilation errors prevent execution even if an older `.ps1` exists. Windows PowerShell's `powershell` must be on PATH.

Use `rukbat run input.rukbat -- arguments...` to pass everything after `--` to the program.
Rukbat exposes these values through `args(): string[]`, returning an empty array when no arguments are supplied.
To run a generated script directly, use `powershell -File input.ps1 arguments...`.

`build` and `run` write a `.ps1` with the entry file's base name in the same directory, replacing existing output on success.
Syntax, type, and import errors leave existing output unchanged. Always check the compiler exit code in automation: stale output may remain.
`check` generates no PowerShell and leaves existing `.ps1` files untouched.
`fmt` accepts a `.rukbat` file or a directory and uses canonical four-space indentation while preserving comments. `rukbat fmt .` recursively formats sources under the current directory, excluding `.git`, `bin`, `obj`, and symbolic links/junctions. An empty directory succeeds. Files with syntax or I/O errors are reported, and remaining files are still processed; successful files are rewritten and invalid sources stay unchanged. The command returns `1` if any source has a syntax error, or `2` if an I/O error occurs.
Blank lines between statements are preserved, with consecutive blank lines collapsed to one. Every formatted file ends with one blank line (`\n\n` after nonempty content).
Formatting also inserts blank lines around functions, struct declarations, and control-flow statements, and after import groups. Related simple statements remain adjacent, and comments stay with their statements.
Compiler success is `0`, compilation errors are `1`, and CLI argument or I/O errors are `2`.
Diagnostics use `file(line,column): error RUKxxxx: message`. Errors in imported files identify the location in that file.
Compilation never executes Rukbat or PowerShell user code.

To publish the CLI:

```powershell
dotnet publish src/Rukbat -c Release -o artifacts/rukbat
.\artifacts\rukbat\rukbat.exe build examples/hello.rukbat
```

## Principles and current scope

- Prefer consistent syntax that is easy to read and write.
- Named imports such as `import files` load standard modules bundled with the compiler.
- The v0.1 standard modules are `files`, `directories`, `path`, `json`, `csv`, `env`, `process`, and `text`. Their APIs and external-data contracts are defined in the [standard library specification](docs/STDLIB.md).
- `exit(code)` terminates the process with a nonnegative `int` exit code. `eprint(text)` writes a line to standard error.
- `text.from_int/from_float/from_bool` and `text.parse_int/parse_float/parse_bool` provide explicit, culture-independent string conversion and parsing.
- `import "./models/user.rukbat" as model` loads a module relative to the importing file, including across directories.
- Variables and functions are isolated per module. Shared dependencies are initialized once.
- Rukbat functions return only explicitly returned values. Intermediate expression values are discarded.
- `print(value)` produces visible output without contributing to a function's return value.
- PowerShell interoperability uses `powershell<T>(...)`.

The original prototype's PowerShell-superset approach is no longer supported.
Sources using the old `$name: string` syntax or raw PowerShell command syntax are rejected.
Consistent Rukbat syntax and behavior take priority over syntax compatibility with existing `.ps1` files.

Current types are `int`, finite binary64 `float`, `string`, `bool`, return-only `void`, built-in `Error`, user-defined `struct`, typed arrays `T[]`, and nullable types `T?`.
Only `T?` can hold `null`. After an explicit check such as `if value != null`, a simple variable can be used as `T`.
The only implicit numeric conversion is safe widening from `int` to `float`. Division by zero and non-finite floats cause Error.
Structs are constructed with named arguments and support fields and internal `fn` methods.
Assignment, arguments, and return values copy nested fields by value. A method's `self` updates the variable on which it was called.
See `examples/value-semantics.rukbat` for a demonstration that the original value remains unchanged after copying.
Functions and struct methods use `fn`; `func` and `function` are rejected.
Arrays support literals such as `[1, 2]`, indexed reads and writes, `.length`, `.append(value)`, and `for item in items`.
Control flow also includes `while condition`, `break`, and `continue`.
`powershell<User[]>` validates and imports multiple external objects. Arrays copy nested elements by value as well.
Use `name := "Taro"` for an inferred declaration. `:=` only declares a new variable; it does not assign to an existing variable.
General-purpose object types, union types, pipeline parsing, and package management are not implemented.
See the [language specification](docs/LANGUAGE.md) for syntax, module rules, and limitations.

## Organizing code across directories

```text
app/
├── main.rukbat
├── models/
│   └── user.rukbat
└── services/
    └── users.rukbat
```

`main.rukbat`:

```rukbat
import "./models/user.rukbat" as model
import "./services/users.rukbat" as users

user: model.User = users.load()
print(user.name)
```

From `services/users.rukbat`, use `import "../models/user.rukbat" as model` to share the same type.
String import paths require a `.rukbat` extension and an explicit `as alias`. Use `/` separators even on Windows.
Local modules always use a string path and an alias, so a local filename matching a standard module does not change import behavior.
Normalized absolute paths identify modules: different aliases or relative paths to the same file still initialize it only once.
The output remains a single `.ps1`.

Run the example:

```powershell
.\rukbat.cmd run examples/app/main.rukbat
```

## Example CLI: exporting a file list as JSON

`examples/file-browser` filters files directly inside a directory supplied as an argument and outputs their names as a JSON array.
It separates the entry point, argument validation, file listing, and shared library operations into modules.

```powershell
# Write direct-child Markdown filenames, such as README.md, to stdout:
.\rukbat.cmd run examples/file-browser/main.rukbat -- . --pattern '*.md'
# Save to a JSON file:
.\rukbat.cmd run examples/file-browser/main.rukbat -- . --pattern '*.md' --output files.json
# Show help:
.\rukbat.cmd run examples/file-browser/main.rukbat -- --help
```

```json
["README.md"]
```

The first argument is the directory. `--pattern` defaults to `*` and uses the PowerShell file-system filter.
Listing is not recursive, normally excludes hidden files, and does not guarantee order.
Zero matches produce `[]`; a single match still produces an array. Normal stdout contains JSON only.
With `--output`, stdout is empty and the file is UTF-8 without a BOM or trailing newline.
Paths are relative to the runtime working directory. Quote paths and patterns containing spaces.
Missing arguments, unknown options, directory access failures, and save failures produce a message on stderr and exit code `1`.
Success and help use exit code `0`.

File operations and JSON encoding use the compiler's `files` and `json` standard modules.
Only imported standard-library implementations are embedded in the generated script; no separate library files are needed at deployment.

## Arrays, loops, and external data

`examples/arrays.rukbat` demonstrates array updates and loops. `examples/import-data.rukbat` imports an array of structs from JSON.

```powershell
.\rukbat.cmd run examples/import-data.rukbat
```

```rukbat
struct User {
    name: string
    age: int
}

users: User[] = powershell<User[]>(
    "ConvertFrom-Json",
    '[{"name":"Taro","age":30},{"name":"Hanako","age":25}]'
)
for user in users {
    print(user.name)
}
```

Missing fields, nulls, and type mismatches produce import errors identifying the location and field.
Arrays are mutable values. `items.append(value)` adds an element, while assignment, arguments, and returns still copy nested values.
Element removal and sending structs to PowerShell are not supported.

## Recovering from errors

Use `try / catch / throw` with the built-in `Error`, which has a `message: string` field.

```rukbat
try {
    throw Error {message: "Operation failed"}
} catch err {
    print(err.message)
}
```

The same catch handles PowerShell errors and type-import failures.
Rethrowing with `throw err` preserves the original location.
Unhandled errors report the Rukbat file, line, and message on stderr and terminate with exit code `1`.
For external executables called directly through `powershell<T>`, exit code `0` means success and any other code causes Error.

The `process.run(executable, arguments, timeout_ms)` API captures stdout and stderr and returns nonzero child exit codes as normal results. A positive deadline covers the process tree and output capture; `null` is deliberately unbounded. Windows Job Objects terminate the tree on timeout, capture failure, or host termination. This requires Windows 10 / Server 2016 or later. Input is closed and each output stream is limited to 64 MiB. See [the process contract](docs/STDLIB.md#process) for containment and cleanup details.
The initial specification allows one catch clause and has no typed catches, `finally`, or `throws` declarations.

```powershell
.\rukbat.cmd run examples/errors.rukbat
```

## Implementation and verification

The compiler follows `Lexer → Parser → AST → ModuleLoader / TypeChecker → Emitter`.
A dedicated lexer and recursive-descent parser produce the AST, where names and types are checked.
The official `System.Management.Automation` library validates generated PowerShell syntax.
Compilation does not rely on regex substitution or executing input scripts.

```powershell
dotnet test
```

Tests cover syntax, types, diagnostic locations, and the CLI, and execute generated `.ps1` files on Windows PowerShell 5.1 and PowerShell 7.
Both `powershell` and `pwsh` must be on PATH. Execution tests are never silently skipped.
Coverage includes struct value copies and `self` updates, explicit returns, scopes, PowerShell type boundaries, shared module initialization, and execution without source files.

## Filtering a text log

The [log-filter example](examples/filter-errors.rukbat) reads a UTF-8 log, splits Windows or Unix line
endings, trims each line, and saves lines starting with the case-sensitive prefix `ERROR:`.

For a legacy Japanese Windows CSV, select CP932 explicitly:

```rukbat
text := files.read("legacy.csv", "windows-932")
rows := csv.parse(text)
```

```powershell
.\rukbat.cmd run examples/filter-errors.rukbat -- application.log errors.log
```

The output uses CRLF between lines, with no trailing newline or BOM. No matching lines produce an empty
file. An existing output file is replaced; its parent directory must exist. Relative paths use the
caller's working directory. Success exits `0`, an incorrect argument count exits `2`, and a read/write
failure exits `1` with a diagnostic on stderr.

The `text` module also provides literal `contains`, `ends_with`, `replace_all`, and `split` operations.
See the [text API contracts and examples](docs/STDLIB.md#text).

## Batch validation acceptance example

The implemented [batch validation acceptance example](examples/batch-check/main.rukbat) follows the [CLI acceptance scenario](docs/CLI_ACCEPTANCE.md). Its end-to-end tests cover the contract from configuration through checker execution and report output.

## Batch organization acceptance example

The [batch organization acceptance example](examples/batch-organize/main.rukbat) follows the [batch organization contract](docs/BATCH_ORGANIZE_ACCEPTANCE.md). It parses a CSV move plan and uses `files.move_batch` for locked, journaled moves on one local NTFS volume. Interrupted work can be recovered or retried, and committed plans can be replayed without repeating moves. See [unattended operation](docs/UNATTENDED.md) for the supported environment, recovery procedure, and fault tests.

The [operations toolkit](docs/OPERATIONS.md) packages versioned releases, runs them with a whole-job deadline and exclusive job lock, records outcomes, generates scheduled tasks, and exposes a JSON health probe with notification integration. It includes file, CLI and Windows administration acceptance examples. Target-account scheduling, backup restoration and production alert delivery remain explicit deployment acceptance checks.
