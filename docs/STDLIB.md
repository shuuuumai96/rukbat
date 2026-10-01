# Rukbat Standard Library

## Confirmed v0.1 specification

The standard library is part of the compiler version. A standard module is imported by its canonical name and cannot be aliased:

```rukbat
import files
import directories
import path
import csv
import json
import env
import process
import text
```

A local module instead uses an explicit relative `.rukbat` path and an alias. Standard module names are reserved as aliases. Generated scripts contain only the standard-library runtime sections used by their imports and remain standalone on Windows PowerShell 5.1.

All failures described below are ordinary Rukbat runtime `Error` values and can be handled with `try` / `catch`.

## Admission criteria

Standard operations must be common in Windows automation and repeatedly useful in Rukbat programs.
They must have explicit, statically checked input/output contracts and predictable failure behavior.
They should run on Windows PowerShell 5.1 without additional deployment dependencies.
Use one canonical API per operation. Rukbat's standard library does not aim to provide a comprehensive wrapper for PowerShell APIs.

## Standard output and error

The built-in `print(value)` and `eprint(value)` write to stdout and stderr respectively, with identical type checks and one appended platform newline. Neither requires an import. Explicit string formatting uses `text.from_*`. Output alone does not change the exit code.

## `text`

### Text operations

Use `text.operation(value, ...)` with the value first. All arguments are positional and required.
Operations return a new value without changing their arguments. There are no option flags or aliases.
All matching is literal, case-sensitive, ordinal, and independent of the OS culture.
There is no regular-expression or wildcard interpretation and no Unicode normalization.
Nullable strings must be narrowed before use; `null` is not an empty string.

| Signature | Example | Result |
|---|---|---|
| `text.trim(value: string): string` | `text.trim("  東京  ")` | `"東京"` |
| `text.contains(value: string, search: string): bool` | `text.contains("接続エラー", "エラー")` | `true` |
| `text.starts_with(value: string, prefix: string): bool` | `text.starts_with("report.csv", "report")` | `true` |
| `text.ends_with(value: string, suffix: string): bool` | `text.ends_with("report.csv", ".csv")` | `true` |
| `text.replace_all(value: string, search: string, replacement: string): string` | `text.replace_all("a-b-c", "-", "_")` | `"a_b_c"` |
| `text.split(value: string, separator: string): string[]` | `text.split("赤,青,緑", ",")` | `["赤", "青", "緑"]` |
| `text.split_lines(value: string): string[]` | `text.split_lines("赤\r\n青\n緑")` | `["赤", "青", "緑"]` |
| `text.join(values: string[], separator: string): string` | `text.join(["赤", "青"], ",")` | `"赤,青"` |

`trim` removes only leading and trailing whitespace. Internal whitespace remains unchanged.
Its fixed whitespace set is U+0009–U+000D, U+0020, U+0085, U+00A0, U+1680,
U+2000–U+200A, U+2028, U+2029, U+202F, U+205F, and U+3000.
This includes tabs, line endings, and full-width spaces. U+180E, U+200B (zero-width space),
and U+FEFF (BOM) are not removed. Empty or whitespace-only input returns `""`.
The set is fixed so PowerShell 5.1 and 7 agree regardless of runtime Unicode tables.

`contains`, `starts_with`, and `ends_with` return `true` for an empty search, prefix, or suffix,
including when `value` is empty. A nonempty search never matches an empty value.

`replace_all` replaces non-overlapping occurrences from left to right. Replacement text is literal
and is not searched again: replacing `"a"` with `"aa"` in `"a"` returns `"aa"`.
Replacing `"aa"` with `"b"` in `"aaa"` returns `"ba"`. An empty replacement deletes matches.
No match returns the original value. An empty `search` raises Error, even for empty input.

`split` treats its entire separator as one literal string, matches from left to right without overlap,
and preserves all empty fields and whitespace. `text.split("a,,b,", ",")` returns `["a", "", "b", ""]`.
An absent separator returns `[value]`; empty input returns `[""]`. An empty separator raises Error.
This is simple delimiter splitting; use `csv.parse` for CSV quoting and escaped fields.

`split_lines` recognizes CRLF as one line ending, and also recognizes standalone LF and CR.
It removes the line endings while preserving line contents and blank lines. Empty input returns `[]`.
A final line ending terminates the last line without creating an extra line:
`"a\n"` returns `["a"]`, `"\n"` returns `[""]`, and `"a\n\n"` returns `["a", ""]`.
Other Unicode separators, including U+0085, U+2028, and U+2029, remain part of the line.

`join` places the literal separator between elements, never after the last one. It preserves empty
elements. An empty array returns `""`; one element returns that element; an empty separator concatenates
the elements directly. For a nonempty separator, joining the result of `split` with that same separator
reconstructs the original input exactly.

These operations intentionally cover reading, filtering, and rewriting ordinary logs and delimited text.
`replace_all` states the replacement scope in its name; `split_lines` gives mixed Windows/Unix line endings
one canonical operation. No new language syntax, overloads, boolean mode flags, string methods, or regex
API are introduced. Character indexing, length, slicing, and case conversion are outside this addition.

See [the log-filter example](../examples/filter-errors.rukbat) for a procedural file-processing workflow.

### Conversion and parsing

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
| `files.list(directory: string, pattern: string): string[]` | Returns absolute paths of directly contained, non-Hidden files that match the file-system wildcard `pattern`. It excludes directories, does not recurse, and does not guarantee order. | An inaccessible or nonexistent directory, or an invalid pattern, causes Error. |
| `files.list_recursive(directory: string, pattern: string): string[]` | Returns absolute paths to files matching the filename wildcard `pattern` at every depth beneath `directory`, including direct children. It excludes directories and does not guarantee order. | An inaccessible/nonexistent directory, a file passed as `directory`, an invalid path or pattern, or a traversal failure causes Error. |
| `files.read(path: string, encoding: string): string` | Reads text with one explicit canonical encoding. | Missing/inaccessible files, an unsupported encoding, an invalid byte sequence, or a conflicting UTF-16 BOM causes Error. |
| `files.write(path: string, text: string, encoding: string): void` | Replaces a file with one explicit canonical encoding, without a BOM or implicit newline. It does not create parent directories. Complete new contents are written to a temporary file in the destination directory before it is moved or used to replace the destination. | An inaccessible path, missing parent directory, unsupported encoding, or unencodable text causes Error. |
| `files.exists(path: string): bool` | Returns `true` for an existing file. Missing files, directories, invalid paths, and access failures or other cases that File.Exists cannot determine return `false`. | This predicate returns `false` rather than raising Error when path resolution or existence checking fails. |
| `files.copy(source: string, destination: string): void` | Copies one file without overwriting an existing destination or creating its parent directory. | A missing, inaccessible, or directory source; an existing destination; a missing parent; identical source and destination; or an I/O failure causes Error. |
| `files.move(source: string, destination: string): void` | Moves one file. Moving within one directory renames it. It does not overwrite an existing destination or create its parent directory. On success the source no longer exists. | A missing or directory source; an existing destination; a missing parent; identical source and destination; or an I/O failure causes Error. Cross-volume moves are allowed but are not atomic; after an error, the destination may remain. |
| `files.remove(path: string): void` | Deletes one file. Removing a missing file succeeds. | A directory, readonly or in-use file, invalid path, or I/O failure causes Error. A file reparse point removes the entry, not its target. |

`encoding` must be exactly one of these case-sensitive names; aliases are not accepted: `utf-8`, `utf-16le`, `utf-16be`, or `windows-932`. `utf-8` accepts and removes only a leading UTF-8 BOM on read. `utf-16le` and `utf-16be` accept and remove their matching leading BOM, accept no BOM, and reject the opposite-endian BOM. All writes are strict and BOM-free. `windows-932` means Windows code page 932 (CP932 / Windows Shift-JIS), for Shift-JIS-compatible text commonly used by legacy Japanese Windows software. It is deliberately CP932 rather than a claim to implement every Shift_JIS variant, and it has no BOM handling. Every encoding rejects invalid input and unencodable output rather than replacing data.

Relative paths for `files` and `directories` are resolved from the filesystem working directory captured at entry-script startup, not the script directory or a later PowerShell location. `files.write` first validates and encodes its complete contents to bytes, then writes those bytes to a uniquely named temporary file in the destination directory. It then uses `File.Replace` for an existing destination or `File.Move` for a missing one. Failures before that final filesystem operation leave an existing destination unchanged. A concurrent creator that wins the missing-destination race is replaced through `File.Replace`, not deleted. Rukbat does not guarantee durability against process, system, storage, or filesystem failure during the final operation, nor does it guarantee atomic behavior on every filesystem. `File.Replace` can merge destination metadata such as attributes and ACLs and can fail while doing so; `files.write` makes no metadata or ACL preservation guarantee.

`files.list` and `files.list_recursive` return absolute paths. `list` considers only direct children; `list_recursive` considers every depth. Both treat `directory` as a literal path. Their nonempty `pattern` applies only to each filename, using the same file-system filter behavior; the supported simple forms include `*` and `?`. It cannot contain `/` or `\\`, so it cannot express directory hierarchy globs. `list` excludes Hidden files. `list_recursive` excludes Hidden files and directories, including hidden subtrees. System attributes do not exclude entries. A directory reparse point explicitly supplied as the `list_recursive` root is a valid traversal root. ReparsePoint entries discovered beneath that root are excluded, so that operation neither returns nor recurses into child symbolic links or junctions. Enumeration returns no partial result when a platform error occurs.

`windows-932` reads use the Windows code-page mapping with strict validation, including NEC-selected IBM and IBM extension byte sequences for the same character (for example, `EE E0` and `FB FC` both decode to U+9AD9). A valid read does not imply that re-encoding reproduces the original bytes. The generated script embeds its decoder and requires no additional installation.

## `directories`

| Signature | Meaning | Failure |
|---|---|---|
| `directories.create(path: string): void` | Creates the directory and any missing parent directories. Creating an existing directory succeeds. | A file at the target path, an invalid path, or an I/O failure causes Error. |
| `directories.exists(path: string): bool` | Returns `true` only for an existing reachable directory. Files, missing paths, invalid paths, inaccessible paths, and indeterminate checks return `false`. | This predicate returns `false` rather than raising Error. |
| `directories.remove(path: string): void` | Deletes an empty directory. Removing a missing directory succeeds. | A file, a non-empty directory, a filesystem root, the captured working directory or its ancestor, or an I/O failure causes Error. A directory reparse point removes the entry, not its target. |
| `directories.remove_all(path: string): void` | Deletes a directory and its ordinary subtree. Removing a missing directory succeeds. | A file, a filesystem root, the captured working directory or its ancestor, or an I/O failure causes Error. Partial deletion is possible. Reparse points at the target or beneath it are removed as entries and are never traversed. |

## `path`

| Signature | Meaning | Failure |
|---|---|---|
| `path.join(parts: string[]): string` | Joins one or more path components using the target platform's path rules. | An empty array or an invalid component causes Error. |
| `path.filename(value: string): string` | Returns the final file-name component, or `""` when the path ends in a directory separator. | An invalid path causes Error. |
| `path.extension(value: string): string` | Returns the final component's extension including its leading `.`, or `""` when none exists. | An invalid path causes Error. |
| `path.parent(value: string): string` | Returns the lexical parent without checking the filesystem or resolving relative paths. It returns `"."` when there is no parent component and preserves a root path. Trailing separators are ignored except for a root. | Empty, whitespace-only, or invalid paths cause Error. |
| `path.equal(left: string, right: string): bool` | Resolves both paths against the captured entry working directory and compares the resulting paths with Windows ordinal case-insensitive comparison. It does not require either path to exist. | Invalid, empty, whitespace-only, or drive-relative paths cause Error. |

`join`, `filename`, `extension`, and `parent` manipulate strings without resolving relative paths or requiring existence. `parent("C:child")` is `"C:"`; filesystem operations reject that drive-relative spelling. A UNC share root is preserved with its trailing separator. `join` may return a relative path; its later use by a standard API resolves against the captured working directory.

`path.script_dir(): string` returns the absolute directory containing the executing `.ps1`, captured from `$PSScriptRoot` before module initialization. It works inside imported functions and follows the deployed script location, not the original source location.

```rukbat
config := path.join([path.script_dir(), "config.json"])
```

`equal` compares resolved path spelling, not filesystem object identity. It does not resolve symbolic links, junctions, hard links, short names, or filesystem object IDs.

## `csv`

| Signature | Meaning | Failure |
|---|---|---|
| `csv.parse(text: string): string[][]` | Parses CSV records and fields as strings. It does not infer types, interpret headers, require a rectangular table, or map records to structs. | Invalid CSV syntax causes Error. |

The separator is comma. CRLF and LF separate records; a final separator is optional. Quoted fields preserve commas and newlines, and `""` inside a quoted field becomes `"`. Whitespace and Unicode are preserved. Empty fields, including quoted empty fields, return `""`. An empty input returns `[]`; a blank record returns `[""]` and is not discarded. A bare CR outside a quoted field, an unquoted quote, an unclosed quote, or non-separator content after a closing quote causes Error. `csv.parse` accepts a Rukbat string and does not decode text encodings or treat U+FEFF specially; select the file encoding with `files.read`, for example `csv.parse(files.read("legacy.csv", "windows-932"))`.

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
| `process.run(executable: string, arguments: string[], timeout_ms: int?): process.Result` | Starts an executable directly, waits for it, and captures standard output and standard error separately. `null` waits without a deadline; a positive value is a millisecond deadline. | A nonpositive timeout or failure to launch, capture, decode, wait, or finish before the deadline causes Error. |

The API does not interpret a shell command string. The executable and each argument are separate values. A nonzero exit code is a normal `Result`, not an Rukbat Error. Output is decoded as strict UTF-8 and is returned exactly, including any trailing newline; zero output becomes `""`. Standard output and standard error are drained concurrently to avoid pipe deadlocks.

A nonpositive timeout is rejected before the child is launched. A timeout is measured from successful process start until process exit. On timeout, Rukbat terminates the directly started root process and raises Error; it does not return partial output. Descendant-process termination is not guaranteed. Timeout cleanup is bounded even when a descendant retains inherited output handles.

Child processes start in the captured entry working directory. Executable paths containing a separator or drive prefix are resolved against that directory. Bare executable names are searched in the captured directory, then the current process PATH; an extensionless name uses `.exe`. Relative PATH entries use the captured directory as well. Each Rukbat string argument corresponds to exactly one argument as observed by the child process's standard C/C++ `argv` parser. The generated code handles Windows command-line quoting and escaping to preserve this guarantee. Rukbat does not guess which strings represent paths. In v0.1, the executable must be a directly launchable binary such as `.exe`. Batch files (`.cmd`, `.bat`) are not supported because their argument-passing rules cannot preserve the one-argument-per-string contract. Executable or environment changes remain the caller's responsibility.

## Runtime path resolution

Standard filesystem operations resolve nonempty relative paths against the captured entry working directory before calling PowerShell or .NET. Absolute paths stay absolute. A root-relative path such as `\data\file.txt` uses the captured directory's drive root. Drive-relative forms such as `C:file.txt` are rejected because they depend on implicit per-drive state; `files.exists` and `directories.exists` return `false` for them. Invalid/empty operation paths raise Error, except for these existence predicates. This does not change the Error contracts of read, write, list, copy, move, create, or remove.

## Program arguments

`args(): string[]` returns command-line arguments supplied to the entry program in order. It returns `[]` when none were supplied. Argument parsing belongs to the program.
