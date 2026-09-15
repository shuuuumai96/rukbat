# Rukbat Standard Library

## Confirmed v0.1 specification

The standard library is part of the compiler version. A standard module is imported by its canonical name and cannot be aliased:

```rukbat
import files
import path
import json
import env
import process
import text
import stderr
```

A local module instead uses an explicit relative `.rukbat` path and an alias. Standard module names are reserved as aliases. Generated scripts contain only the standard-library runtime sections used by their imports and remain standalone on Windows PowerShell 5.1.

All failures described below are ordinary Rukbat runtime `Error` values and can be handled with `try` / `catch`.

## Admission criteria

Standard operations must be common in Windows automation and repeatedly useful in Rukbat programs.
They must have explicit, statically checked input/output contracts and predictable failure behavior.
They should run on Windows PowerShell 5.1 without additional deployment dependencies.
Use one canonical API per operation. Rukbat's standard library does not aim to provide a comprehensive wrapper for PowerShell APIs.

## `stderr`

`stderr.print(text: string): void` writes the text followed by a newline to standard error.
It does not write to standard output and does not raise Error merely because a message was printed.

## `text`

| Signature | Meaning |
|---|---|
| `text.from_int(value: int): string` | Signed decimal representation. |
| `text.from_float(value: float): string` | Culture-independent round-trip representation of a finite binary64 value. |
| `text.from_bool(value: bool): string` | Lowercase `true` or `false`. |
| `text.parse_int(value: string): int` | Parses a signed decimal integer within the signed 32-bit range. |
| `text.parse_float(value: string): float` | Parses a finite decimal binary64 value. |
| `text.parse_bool(value: string): bool` | Accepts only case-sensitive `true` and `false`. |

All parse operations trim leading and trailing ASCII whitespace (U+0009 through U+000D and U+0020).
Other whitespace is rejected. Numeric parsing is independent of the OS locale and uses ASCII digits.
Integer syntax is `[+-]?[0-9]+`; decimal points and exponents are rejected.
Float syntax is `[+-]?[0-9]+(\.[0-9]+)?([eE][+-]?[0-9]+)?`: the decimal separator is a dot,
and `.5`, `1.`, grouping separators, NaN and Infinity are rejected.
Invalid input, overflow, and nonzero float input that underflows to zero cause Error.
Representable subnormal floats are accepted. Float formatting preserves the numeric value on reparse;
it does not promise to preserve the input spelling or the sign of zero.

## `files`

| Signature | Meaning | Failure |
|---|---|---|
| `files.list(directory: string, pattern: string): string[]` | Returns file names directly contained by `directory` that match the file-system wildcard `pattern`. It excludes directories, does not recurse, and does not guarantee order. | An inaccessible or nonexistent directory, or an invalid pattern, causes Error. |
| `files.read(path: string): string` | Reads UTF-8 text. A UTF-8 BOM is accepted and removed from the returned text. | Missing/inaccessible files and invalid UTF-8 cause Error. |
| `files.write(path: string, text: string): void` | Replaces a file with UTF-8 text without a BOM. It adds no newline and does not create parent directories. | An inaccessible path or missing parent directory causes Error. |
| `files.exists(path: string): bool` | Returns whether `path` identifies an existing file. Directories return `false`. | Platform-level failures cause Error. |

Relative paths are resolved from the Rukbat process working directory. `files.write` is not an atomic replacement operation.

## `path`

| Signature | Meaning | Failure |
|---|---|---|
| `path.join(parts: string[]): string` | Joins one or more path components using the target platform's path rules. | An empty array or an invalid component causes Error. |
| `path.filename(value: string): string` | Returns the final file-name component, or `""` when the path ends in a directory separator. | An invalid path causes Error. |
| `path.extension(value: string): string` | Returns the final component's extension including its leading `.`, or `""` when none exists. | An invalid path causes Error. |

These operations manipulate strings. They do not require the referenced file or directory to exist.

## `json`

| Signature | Meaning | Failure |
|---|---|---|
| `json.encode(value: T): string` | Encodes a supported typed value as compact JSON. | An unsupported value or non-finite float causes Error. |
| `json.decode<T>(text: string): T` | Decodes JSON as the explicitly requested Rukbat type. | Invalid JSON or any mismatch with `T` causes Error. |

`T` may be `int`, `float`, `string`, `bool`, a struct, an array of supported values, or a nullable form of a supported type. `<T>` is a dedicated explicit result type like `powershell<T>`; it does not introduce general-purpose generics.

Decoding is strict:

- property names are case-sensitive and duplicate properties cause Error;
- every declared struct field must be present, including nullable fields;
- extra properties are ignored;
- JSON `null` is accepted only by `T?`;
- a JSON number converts to `int` only when its lexical form has no decimal point or exponent and its value fits `int` exactly;
- a JSON number converts to `float` only when the result is finite;
- strings, booleans, arrays, and objects are not coerced to other kinds.

Encoding uses compact output and emits struct properties in declaration order. Nullable values encode as either their contained value or JSON `null`.

## `env`

| Signature | Meaning | Failure |
|---|---|---|
| `env.get(name: string): string?` | Reads the current Rukbat process environment. An undefined name returns `null`; a defined empty value returns `""`. | Invalid platform-level access causes Error. |
| `env.set(name: string, value: string): void` | Sets a non-empty value in the current Rukbat process environment. | An empty value or invalid platform-level access causes Error. |
| `env.remove(name: string): void` | Removes a name from the current Rukbat process environment. Removing an undefined name succeeds. | Invalid platform-level access causes Error. |

Ordinary environment-variable operations in Windows PowerShell 5.1 / .NET Framework treat setting an empty string as removal. Because the Windows PowerShell 5.1-compatible runtime cannot safely represent setting an empty value, `env.set(name, "")` causes Rukbat Error. Use `env.remove(name)` for removal. If an empty environment value is inherited from a parent process, `env.get` reads it as `""`. No Win32 API calls are added solely to support setting empty values.

Changes affect the current process and are inherited by subsequently started child processes. They do not modify persistent user or machine environment settings.

## `process`

The module exports this result struct:

```rukbat
struct Result {
    exit_code: int
    stdout: string
    stderr: string
}
```

| Signature | Meaning | Failure |
|---|---|---|
| `process.run(executable: string, arguments: string[]): process.Result` | Starts an executable directly, waits for it, and captures standard output and standard error separately. | Failure to launch, capture, decode, or wait for the process causes Error. |

The API does not interpret a shell command string. The executable and each argument are separate values. A nonzero exit code is a normal `Result`, not an Rukbat Error. Output is decoded as strict UTF-8 and is returned exactly, including any trailing newline; zero output becomes `""`. Standard output and standard error are drained concurrently to avoid pipe deadlocks.

## Program arguments

`args(): string[]` returns command-line arguments supplied to the entry program in order. It returns `[]` when none were supplied. Argument parsing belongs to the program.
