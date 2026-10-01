# Rukbat Standard Library

## Confirmed v0.1 specification

The standard library is part of the compiler version. A standard module is imported by its canonical name and cannot be aliased:

```rukbat
import fs
import path
import csv
import json
import env
import process
import text
```

A local module instead uses an explicit relative `.rukbat` path and an alias. Standard module names are reserved as aliases. Generated scripts contain only the standard-library runtime sections used by their imports and remain standalone on Windows PowerShell 5.1.

All failures described below are ordinary Rukbat runtime `Error` values and can be handled with `try` / `catch`.

## Choosing filesystem and path operations

Use `fs` (filesystem) to inspect or change actual files and directories. Use `path` to construct,
decompose, and compare path values without inspecting filesystem entries. Both use strings;
no special path object or method chaining is required. A file need not exist to use `path.filename`
or `path.parent`. `path.script_dir` reads the deployed script directory captured at startup;
`path.equal` and `path.find_duplicate` use the captured working directory to resolve path spelling,
not to compare the identity of filesystem entries.

| Task | Canonical operation | Important boundary |
|---|---|---|
| Read or replace text | `fs.read_text`, `fs.write_text` | Encoding is required. A write replaces a file; it does not append or create parent directories. |
| List matching files | `fs.list_files`, `fs.list_files_recursive` | Only files are returned. `_recursive` includes subdirectories. |
| Check the target kind | `fs.is_file`, `fs.is_dir` | Missing or inaccessible entries return `false`. These checks do not guarantee a later operation will succeed. |
| Create a directory and its parents | `fs.create_dir_all` | An existing directory succeeds; a file at that path is an Error. |
| Copy or move one file | `fs.copy_file`, `fs.move_file` | Destination is the complete file path, not a directory. An existing destination is never overwritten; parents must exist. |
| Remove one file | `fs.remove_file` | A directory is an Error. |
| Remove an empty directory | `fs.remove_dir` | A file or a nonempty directory is an Error. |
| Remove a directory tree | `fs.remove_dir_all` | Explicit recursive deletion; directory links are not traversed. |
| Execute or recover a journaled file move plan | `fs.move_files_batch`, `fs.recover_file_moves` | Specialized same-drive, local NTFS operation; see the recovery contract below. |
| Construct or inspect a path value | `path.join`, `path.filename`, `path.extension`, `path.parent` | These operations do not check existence. |
| Compare resolved path spelling | `path.equal`, `path.find_duplicate` | Windows case-insensitive spelling comparison; not file identity. |
| Locate data beside the deployed script | `path.script_dir` | This is the deployed `.ps1` directory, not the working directory or source directory. |

All arguments are required and positional. Operations have no aliases, optional flags, or implicit
file/directory dispatch. `_file` selects one file, `_dir` selects a directory, and `_all` in
`create_dir_all` and `remove_dir_all` makes the parent/subtree scope explicit.

```rukbat
import fs
import path

output := path.join([path.script_dir(), "output"])
fs.create_dir_all(output)

for source in fs.list_files("input", "*.csv") {
    destination := path.join([output, path.filename(source)])
    fs.copy_file(source, destination)
}
```

Here `input` is relative to the entry working directory, while `output` is beside the deployed script.
The current filesystem API does not provide directory listing, directory copy/move, raw byte I/O,
or file metadata. Those operations are not implied by similarly named file functions.

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

## `fs`

The module contains both file and directory operations. Import `fs` once for either kind.

### File operations

| Signature | Meaning | Failure |
|---|---|---|
| `fs.list_files(directory: string, pattern: string): string[]` | Returns absolute paths of directly contained, non-Hidden files that match the file-system wildcard `pattern`. It excludes directories, does not recurse, and does not guarantee order. | An inaccessible or nonexistent directory, or an invalid pattern, causes Error. |
| `fs.list_files_recursive(directory: string, pattern: string): string[]` | Returns absolute paths to files matching the filename wildcard `pattern` at every depth beneath `directory`, including direct children. It excludes directories and does not guarantee order. | An inaccessible/nonexistent directory, a file passed as `directory`, an invalid path or pattern, or a traversal failure causes Error. |
| `fs.read_text(path: string, encoding: string): string` | Reads text with one explicit canonical encoding. | Missing/inaccessible files, an unsupported encoding, an invalid byte sequence, or a conflicting UTF-16 BOM causes Error. |
| `fs.write_text(path: string, text: string, encoding: string): void` | Replaces a file with one explicit canonical encoding, without a BOM or implicit newline. It does not create parent directories. Complete new contents are written to a temporary file in the destination directory before it is moved or used to replace the destination. | An inaccessible path, missing parent directory, unsupported encoding, or unencodable text causes Error. |
| `fs.is_file(path: string): bool` | Returns `true` for an existing file. Missing files, directories, invalid paths, and access failures or other cases that File.Exists cannot determine return `false`. | This predicate returns `false` rather than raising Error when path resolution or existence checking fails. |
| `fs.copy_file(source: string, destination: string): void` | Copies one file without overwriting an existing destination or creating its parent directory. | A missing, inaccessible, or directory source; an existing destination; a missing parent; identical source and destination; or an I/O failure causes Error. |
| `fs.move_file(source: string, destination: string): void` | Moves one file. Moving within one directory renames it. It does not overwrite an existing destination or create its parent directory. On success the source no longer exists. | A missing or directory source; an existing destination; a missing parent; identical source and destination; or an I/O failure causes Error. Cross-volume moves are allowed but are not atomic; after an error, the destination may remain. |
| `fs.remove_file(path: string): void` | Deletes one file. Removing a missing file succeeds. | A directory, readonly or in-use file, invalid path, or I/O failure causes Error. A file reparse point removes the entry, not its target. |

`encoding` must be exactly one of these case-sensitive names; aliases are not accepted: `utf-8`, `utf-16le`, `utf-16be`, or `windows-932`. `utf-8` accepts and removes only a leading UTF-8 BOM on read. `utf-16le` and `utf-16be` accept and remove their matching leading BOM, accept no BOM, and reject the opposite-endian BOM. All writes are strict and BOM-free. `windows-932` means Windows code page 932 (CP932 / Windows Shift-JIS), for Shift-JIS-compatible text commonly used by legacy Japanese Windows software. It is deliberately CP932 rather than a claim to implement every Shift_JIS variant, and it has no BOM handling. Every encoding rejects invalid input and unencodable output rather than replacing data.

Relative paths for `fs` are resolved from the filesystem working directory captured at entry-script startup, not the script directory or a later PowerShell location. `fs.write_text` first validates and encodes its complete contents to bytes, then writes and flushes those bytes to a uniquely named temporary file in the destination directory. It then uses `File.Replace` for an existing destination or `File.Move` for a missing one. Failures before that final filesystem operation leave an existing destination unchanged. A concurrent creator that wins the missing-destination race is replaced through `File.Replace`, not deleted. Rukbat does not guarantee durability against process, system, storage, or filesystem failure during the final operation, nor does it guarantee atomic behavior on every filesystem. `File.Replace` can merge destination metadata such as attributes and ACLs and can fail while doing so; `fs.write_text` makes no metadata or ACL preservation guarantee.

`fs.list_files` and `fs.list_files_recursive` return absolute paths. `list_files` considers only direct children; `list_files_recursive` considers every depth. Both treat `directory` as a literal path. Their nonempty `pattern` applies only to each filename, using the same file-system filter behavior; the supported simple forms include `*` and `?`. It cannot contain `/` or `\\`, so it cannot express directory hierarchy globs. `list_files` excludes Hidden files. `list_files_recursive` excludes Hidden files and directories, including hidden subtrees. System attributes do not exclude entries. A directory reparse point explicitly supplied as the `list_files_recursive` root is a valid traversal root. ReparsePoint entries discovered beneath that root are excluded, so that operation neither returns nor recurses into child symbolic links or junctions. Enumeration returns no partial result when a platform error occurs.

`windows-932` reads use the Windows code-page mapping with strict validation, including NEC-selected IBM and IBM extension byte sequences for the same character (for example, `EE E0` and `FB FC` both decode to U+9AD9). A valid read does not imply that re-encoding reproduces the original bytes. The generated script embeds its decoder and requires no additional installation.

### Directory operations

| Signature | Meaning | Failure |
|---|---|---|
| `fs.create_dir_all(path: string): void` | Creates the directory and any missing parent directories. Creating an existing directory succeeds. | A file at the target path, an invalid path, or an I/O failure causes Error. |
| `fs.is_dir(path: string): bool` | Returns `true` only for an existing reachable directory. Files, missing paths, invalid paths, inaccessible paths, and indeterminate checks return `false`. | This predicate returns `false` rather than raising Error. |
| `fs.remove_dir(path: string): void` | Deletes an empty directory. Removing a missing directory succeeds. | A file, a non-empty directory, a filesystem root, the captured working directory or its ancestor, or an I/O failure causes Error. A directory reparse point removes the entry, not its target. |
| `fs.remove_dir_all(path: string): void` | Deletes a directory and its ordinary subtree. Removing a missing directory succeeds. | A file, a filesystem root, the captured working directory or its ancestor, or an I/O failure causes Error. Partial deletion is possible. Reparse points at the target or beneath it are removed as entries and are never traversed. |

### Recoverable file moves

A move plan is limited to 100000 entries and a 64 MiB serialized plan. Oversized plans are rejected before source mutation; journal reads enforce corresponding size limits.

`fs.move_files_batch(sources: string[], destinations: string[], journal: string): void` moves a complete plan with exclusive file locks and a persistent recovery journal. `fs.recover_file_moves(journal: string): void` rolls an interrupted, uncommitted plan back to its original source paths. These operations require local fixed NTFS storage; every source, destination, and journal must be on the same drive. Reparse points anywhere in their paths, multiply linked files, alternate data streams, overlapping paths, and ambiguous trailing-dot/space paths are rejected. Parent directories for the journal must already exist.

Before the first rename, the implementation locks all source files against writes/deletes, locks their directories against renames/deletes, creates and locks destination directories, records absolute paths, file IDs, creation times and SHA-256 hashes, and flushes the checksummed journal to storage. The journal itself is exclusively locked, so concurrent uses of the same journal fail before moving files. Renames operate on the locked file handles and never replace an existing destination. All destination parents are prepared before any source is moved. Empty directories and an empty journal can remain after a preparation failure.

A normal move failure rolls completed moves back. If rollback also fails, Error identifies the retained journal; do not discard it. After host termination, call `recover_file_moves`, or call `move_files_batch` again with the same plan and journal. Recovery verifies every recorded file's identity and contents before changing any file. A foreign/replaced/modified file, corrupt journal, ambiguous source/destination state, or inaccessible path stops recovery without overwriting data. Recovery can itself be interrupted and repeated. Once rollback has finished, the same `move_files_batch` call can execute the plan again.

The commit marker is flushed after all moves finish. Repeating a committed plan verifies the destinations and returns without moving anything again. A different plan cannot reuse that journal. `recover_file_moves` on a committed plan only verifies it; it does not undo committed work. Retain the journal as the completion record; use a new journal for a genuinely new job. A missing journal is an error for recovery, and a damaged or truncated nonempty journal is never silently reset.

This is crash-recoverable execution, not an atomic snapshot for other readers: they may observe intermediate paths. Created directories are retained. Process-termination recovery is tested; it is not a guarantee against disk corruption or hardware that ignores flush requests. Keep the journal and operated directories under the job account's control. These guarantees apply to `move_files_batch`, not to arbitrary sequences of `fs.move_file`, deletion, or external-program side effects.

## `path`

This module operates on path values without inspecting filesystem entries. Existence checks belong
to `fs.is_file` and `fs.is_dir`; `path.parent` only computes the parent path value.

| Signature | Meaning | Failure |
|---|---|---|
| `path.join(parts: string[]): string` | Joins one or more path components using the target platform's path rules. | An empty array or an invalid component causes Error. |
| `path.filename(value: string): string` | Returns the final file-name component, or `""` when the path ends in a directory separator. | An invalid path causes Error. |
| `path.extension(value: string): string` | Returns the final component's extension including its leading `.`, or `""` when none exists. | An invalid path causes Error. |
| `path.parent(value: string): string` | Returns the lexical parent without checking the filesystem or resolving relative paths. It returns `"."` when there is no parent component and preserves a root path. Trailing separators are ignored except for a root. | Empty, whitespace-only, or invalid paths cause Error. |
| `path.equal(left: string, right: string): bool` | Resolves both paths against the captured entry working directory and compares the resulting paths with Windows ordinal case-insensitive comparison. It does not require either path to exist. | Invalid, empty, whitespace-only, or drive-relative paths cause Error. |
| `path.find_duplicate(paths: string[]): string?` | Returns the original spelling of the first repeated path, using `path.equal` semantics, or `null`. | An invalid path encountered before a duplicate causes Error. |

`join`, `filename`, `extension`, and `parent` manipulate strings without resolving relative paths or requiring existence. `parent("C:child")` is `"C:"`; filesystem operations reject that drive-relative spelling. A UNC share root is preserved with its trailing separator. `join` may return a relative path; its later use by a standard API resolves against the captured working directory.

`path.script_dir(): string` returns the absolute directory containing the executing `.ps1`, captured from `$PSScriptRoot` before module initialization. It works inside imported functions and follows the deployed script location, not the original source location.

```rukbat
config := path.join([path.script_dir(), "config.json"])
```

`equal` compares resolved path spelling, not filesystem object identity. It does not resolve symbolic links, junctions, hard links, short names, or filesystem object IDs.

`path.find_duplicate(paths: string[]): string?` scans paths in input order and returns the original spelling of the first path equal to an earlier path, or `null` when no path repeats (including an empty array). Equality uses the same captured-working-directory resolution and ordinal case-insensitive comparison as `path.equal`; entries need not exist. An invalid path encountered before a duplicate raises Error. Scanning stops at the first duplicate. Each visited path is resolved once and checked against a set, avoiding pairwise comparisons when validating file plans. The input array is not changed.

## Choosing CSV and JSON operations

Choose the representation that matches the input:

| Task | API |
|---|---|
| CSV with a header; access cells by name | `csv.parse_table(text)` returns `csv.Table`; use `csv.get`, `csv.set`, and `csv.stringify_table` |
| CSV without a header, or records with different widths | `csv.parse(text)` returns `string[][]`; write with `csv.stringify(rows)` |
| JSON with unknown or mixed structure | `json.parse(text)` returns `json.Value`; select with `get` / `items`, convert with `as<T>` |
| JSON with a fixed schema | `json.parse<T>(text)` validates that type |
| Update JSON while retaining unknown properties | `json.set` / `remove`, `set_at` / `remove_at` / `append`; assign the returned value |
| Write JSON | `json.stringify(value)` infers the input type |

Parsers accept document contents, not a filename. Stringifiers return document contents without
writing a file. Use `fs.read_text` and `fs.write_text` for file access and explicit encodings.
These APIs accept positional arguments only and have no aliases. Documents are held in memory;
these are not streaming APIs. Serialization preserves data, not the original text layout.

## `csv`

### Header tables

Use this representation for CSV files whose first record contains column names.
`csv.Table` has two public fields: `columns: string[]` in header order and `rows: csv.Row[]`
containing only data rows. `csv.Row` is opaque and immutable; use `get` and `set` to access cells.
Neither type has a public struct literal. Create tables with `parse_table` or `table`, and new
rows with `row`.

| Signature | Meaning | Failure |
|---|---|---|
| `csv.parse_table(text: string): csv.Table` | Parses the header and validates all data rows before returning. | Empty input, empty or duplicate column names, invalid CSV, or unequal row widths cause Error. Width errors identify the one-based CSV record number, including the header. |
| `csv.table(columns: string[], rows: string[][]): csv.Table` | Creates a table from explicit columns and cell strings. `[]` creates a header-only table. | Invalid columns or unequal row widths cause Error. Row numbers start at 1 in the supplied data rows. |
| `csv.row(columns: string[], values: string[]): csv.Row` | Creates one row with the given columns and cell strings. | Invalid columns or a width mismatch causes Error. |
| `csv.get(row: csv.Row, column: string): string` | Reads a cell by its exact, case-sensitive column name. | An unknown column causes Error. |
| `csv.has_column(table: csv.Table, column: string): bool` | Checks the current header for a column, including when there are no data rows. | Invalid table column names cause Error. |
| `csv.set(row: csv.Row, column: string, value: string): csv.Row` | Returns a copy with one cell replaced. Assign the result to keep the change. | An unknown column causes Error. |
| `csv.stringify_table(table: csv.Table): string` | Writes the header followed by data rows with the same escaping as `stringify`. | Invalid table columns or any row whose columns differ in name or order causes Error. |

Names are literal strings: dots, slashes, brackets, spaces, and Unicode have no special meaning.
Names are not trimmed; `A` and `a` are distinct. There must be at least one nonempty column name.
Cells remain strings, including empty strings. Use `text.parse_int`, `text.parse_float`, or
`text.parse_bool` when a script needs a typed value.
Use `has_column` to validate required columns before iterating, or to handle optional columns.

A header-only file returns zero data rows. A blank data record represents one empty field and
must match the header width. Rows keep their order. Tables and their arrays follow ordinary
Rukbat copy semantics. `set` never changes the original row or any table that contains it.
Filter by building a `csv.Row[]`, append a row created from `table.columns`, and update a cell by
assigning the returned row:

```rukbat
import csv
import fs

people := csv.parse_table(fs.read_text("people.csv", "windows-932"))
kept: csv.Row[] = []

for person in people.rows {
    if csv.get(person, "enabled") == "true" {
        kept.append(csv.set(person, "note", "checked"))
    }
}
people.rows = kept
people.rows.append(csv.row(people.columns, ["Hanako", "true", "new"]))
fs.write_text("enabled.csv", csv.stringify_table(people), "utf-8")
```

This example expects columns `name,enabled,note` in that order for the appended row. Named access
works regardless of input column order. To create a different layout, build a new table with
`csv.table` and new rows in that layout. Changing `table.columns` does not rename or reorder
existing rows; `stringify_table` rejects mismatches rather than assigning cells to the wrong
columns. `csv.Row` cannot be converted by `json` or returned by `powershell<T>`; build a normal
struct or string array explicitly when exporting data to another format.

### Raw records

Use raw records when input has no header or intentionally contains different record widths.

| Signature | Meaning | Failure |
|---|---|---|
| `csv.parse(text: string): string[][]` | Parses records as strings without interpreting headers or requiring a rectangular table. | Invalid CSV syntax causes Error. |
| `csv.stringify(rows: string[][]): string` | Converts rows to CSV, escaping commas, quotes, and newlines. | A row with no fields causes Error and identifies its one-based record number. |

The separator is comma. CRLF and LF separate records; a final separator is optional. Quoted fields preserve commas and newlines, and `""` inside a quoted field becomes `"`. Whitespace and Unicode are preserved. Empty fields, including quoted empty fields, return `""`. An empty input returns `[]`; a blank record returns `[""]` and is not discarded. A bare CR outside a quoted field, an unquoted quote, an unclosed quote, or non-separator content after a closing quote causes Error. `csv.parse` accepts a Rukbat string and does not decode text encodings or treat U+FEFF specially; select the file encoding with `fs.read_text`, for example `csv.parse(fs.read_text("legacy.csv", "windows-932"))`.

`stringify` uses commas between fields and CRLF after every record, including the last. Empty fields
and fields containing a comma, quote, CR, or LF are quoted; a quote inside a quoted field becomes
`""`. Other fields are emitted unchanged. `[]` becomes `""`; `[[""]]` becomes `"\"\"\r\n"`.
Rows may have different numbers of fields, but an empty row `[]` is rejected because CSV cannot
represent a record with zero fields. Every representable row survives `parse(stringify(rows))`
without changing its strings or order. Neither operation modifies the input or changes field
contents according to their numeric, boolean, or spreadsheet-like spelling.

With raw records the first record is ordinary data. Use `parse_table` for header validation and
named access. Raw records can explicitly include a header when writing a document:

```rukbat
import csv
import fs

rows: string[][] = [
    ["name", "note"],
    ["Taro", "contains, a comma"],
    ["Hanako", ""]
]
fs.write_text("people.csv", csv.stringify(rows), "utf-8")

loaded := csv.parse(fs.read_text("people.csv", "utf-8"))
print(loaded[1][0]) // Taro
```

## `json`

| Signature | Meaning | Failure |
|---|---|---|
| `json.parse(text: string): json.Value` | Parses a JSON document without requiring a schema or user-defined struct. Preserves every property, array element, and number token. | Invalid JSON or duplicate property names cause Error. |
| `json.parse<T>(text: string): T` | Parses JSON into the explicitly requested Rukbat type. | Invalid JSON or any mismatch with `T` causes Error. |
| `json.stringify(value: T): string` | Converts a supported typed value or `json.Value` to compact JSON text. | An unsupported value or non-finite Rukbat float causes Error. |
| `json.get(value: json.Value, name: string): json.Value` | Returns one object property by its exact, case-sensitive name. | A missing property or a non-object value causes Error. |
| `json.has(value: json.Value, name: string): bool` | Checks whether an object contains the named property, including a property whose value is JSON `null`. | A non-object value causes Error. |
| `json.keys(value: json.Value): string[]` | Returns an object's property names in ordinal sort order. An empty object returns `[]`. | A non-object value causes Error. |
| `json.items(value: json.Value): json.Value[]` | Returns an array's elements in order, including nulls and values of different kinds. An empty array returns `[]`. | A non-array value causes Error. |
| `json.kind(value: json.Value): string` | Returns `"object"`, `"array"`, `"string"`, `"number"`, `"bool"`, or `"null"`. | None. |
| `json.set(value: json.Value, name: string, replacement: T): json.Value` | Returns an object with a property added or replaced. Other properties are retained. | A non-object value causes Error. |
| `json.remove(value: json.Value, name: string): json.Value` | Returns an object with the named property removed. A missing property succeeds. | A non-object value causes Error. |
| `json.set_at(value: json.Value, index: int, replacement: T): json.Value` | Returns an array with one existing element replaced; indices start at zero. | A non-array value or an out-of-range index causes Error. |
| `json.remove_at(value: json.Value, index: int): json.Value` | Returns an array with one element removed; later elements shift left. | A non-array value or an out-of-range index causes Error. |
| `json.append(value: json.Value, replacement: T): json.Value` | Returns an array with one element appended, including to an empty array. | A non-array value causes Error. |
| `json.as<T>(value: json.Value): T` | Converts just the selected JSON value to the explicitly requested Rukbat type, using the same strict rules as `parse<T>`. | Any mismatch with `T` causes Error. |

### Reading JSON without a schema

`json.Value` is an opaque, immutable standard-library value. It represents any JSON value, including
an entire document or a nested property. No struct declaration is required. It can be assigned,
passed to functions, returned, and stored in arrays or struct fields. Its contents are accessed only
through the JSON operations; it has no public fields or constructors. Updates return a new value
and never mutate the original.

Use `get` for properties, `items` for array iteration or indexing, and `as<T>` for only the values
your script actually needs. Property names are literal strings, so dots, slashes, brackets, and an
empty name need no special escaping or path syntax. Use `keys` to inspect unknown object properties.

For a response such as `{"data":{"users":[{"name":"Taro","nickname":null,"extra":{"unknown":1}}]}}`:

```rukbat
import json
import fs

document := json.parse(fs.read_text("response.json", "utf-8"))
data := json.get(document, "data")
users := json.items(json.get(data, "users"))

for user in users {
    name := json.as<string>(json.get(user, "name"))
    print(name)

    if json.has(user, "nickname") {
        nickname := json.as<string?>(json.get(user, "nickname"))

        if nickname != null {
            print(nickname)
        }
    }
}
```

A present JSON `null` is a valid `json.Value` with kind `"null"`. A missing property is different:
`has` returns `false` and `get` raises Error. `json.Value?` additionally permits a Rukbat `null`
for an optional value; this is distinct from a non-null `json.Value` representing JSON `null`.

Numbers remain JSON number tokens until converted. For example, `9007199254740993` and `1e400` can
be parsed, selected, and stringified without precision loss. Conversion to `int` still requires
an exact 32-bit integer, and conversion to `float` still requires a finite binary64 value.
`stringify` preserves numeric spelling, emits object properties in ordinal name order, and preserves
array order. Whitespace and the original spelling of string escapes are not preserved.

`items` and `keys` return ordinary independent Rukbat arrays. Updating those arrays does not change
the original JSON document. Convert a selected object with `as<MyStruct>` when a small part of a
large document has a useful known structure. Known structs may also declare a `json.Value` field
to retain an arbitrary subtree.

### Updating JSON while retaining unknown data

Every update returns a new `json.Value`. Assign that result explicitly. Object names use the same
literal, case-sensitive rules as `get`. `set_at` and `remove_at` require an existing element;
use `append` to add at the end. Replacements may be `json.Value`, primitives, supported structs or
arrays, nullable values, or literal `null`. Replacement types are inferred; updates do not accept
`<T>`. Empty replacement arrays need explicitly typed variables. `append` adds an array replacement
as one element, without flattening it.

Update a nested child first, then assign it back to its parent. Unknown siblings and untouched
subtrees retain their data, including original JSON number tokens. Updating a child alone does
not change its parent. Replacing an entire child intentionally replaces all its contents.

```rukbat
import json
import fs

config := json.parse(fs.read_text("config.json", "utf-8"))
service := json.get(config, "service")
service = json.set(service, "enabled", true)
config = json.set(config, "service", service)
config = json.remove(config, "obsolete")
config = json.set(config, "note", null)

workers := json.get(config, "workers")
worker := json.items(workers)[0]
worker = json.set(worker, "enabled", false)
workers = json.set_at(workers, 0, worker)
workers = json.append(workers, json.parse('{"name":"new","enabled":true}'))
config = json.set(config, "workers", workers)
fs.write_text("updated.json", json.stringify(config), "utf-8")
```

Use schema-free parsing for updates that must retain unknown properties. Converting an object to
a struct with `parse<T>` or `as<T>` ignores undeclared properties; stringifying that struct cannot
restore them. Serialization emits compact text and sorted keys for dynamic objects, so original
whitespace, key order, and escape spelling are not retained.

### Reading a known type

`T` may be `int`, `float`, `string`, `bool`, `json.Value`, a struct, an array of supported values, or
a nullable form of a supported type. `csv.Row` and containers containing it are not supported.
`<T>` on `parse` and `as` is a dedicated result type like `powershell<T>`; it does not introduce
general-purpose generics.

Both `parse<T>` and `as<T>` use strict conversion:

- property names are case-sensitive and duplicate properties cause Error;
- every declared struct field must be present, including nullable fields;
- extra properties are ignored;
- JSON `null` converts to Rukbat `null` only for `T?`; a `json.Value` destination retains it as a JSON value;
- a JSON number converts to `int` only when its lexical form has no decimal point or exponent and its value fits `int` exactly;
- a JSON number converts to `float` only when the result is finite;
- strings, booleans, arrays, and objects are not coerced to other kinds.

`stringify` uses compact output without a trailing newline and emits struct properties in declaration
order. Embedded `json.Value` objects use ordinal property order. Nullable values become either
their contained value or JSON `null`. The input is not modified.
The input type is inferred; `stringify` does not accept `<T>`. Use an explicitly typed variable for
an empty array or `null` so that the value has a concrete type.

```rukbat
import json
import fs

struct Config {
    name: string
    enabled: bool
}

config := Config {
    name: "nightly",
    enabled: true
}
fs.write_text("config.json", json.stringify(config), "utf-8")

loaded := json.parse<Config>(fs.read_text("config.json", "utf-8"))
print(loaded.name)

numbers := json.parse<int[]>("[1,2,3]")
print(numbers[0])
```

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

A nonpositive timeout is rejected before the child is launched. The process is assigned atomically at creation to a Windows Job Object with kill-on-close and no breakaway permission. `process.run` waits for the root and its descendants, and both output captures, using one deadline measured from successful process creation. It is a synchronous operation: it cannot launch a persistent background service. On timeout or capture failure the complete job is terminated. Abrupt termination of the PowerShell host also closes the job and terminates its processes. Cleanup waits up to five seconds for job termination and five seconds for capture completion; cleanup failure raises Error. `null` leaves execution without a deadline and should be reserved for deliberately unbounded operations.

Process containment requires Windows 10 / Server 2016 or later. Failure to establish containment rejects launch; there is no uncontained fallback. Only stdin/stdout/stderr handles are inherited. Standard input is closed (`NUL`), so an unexpected prompt cannot consume job-runner input. Capture is limited to 64 MiB per stream; exceeding it terminates the job and raises Error instead of allowing unbounded memory growth. Nonzero root exit codes remain ordinary results. UTF-8 decoding follows successful capture. The job contains ordinary child-process creation, not work delegated to an already-running service or a remote machine. Termination does not undo side effects already performed by the external program.

Child processes start in the captured entry working directory. Executable paths containing a separator or drive prefix are resolved against that directory. Bare executable names are searched in the captured directory, then the current process PATH; an extensionless name uses `.exe`. Relative PATH entries use the captured directory as well. Each Rukbat string argument corresponds to exactly one argument as observed by the child process's standard C/C++ `argv` parser. The generated code handles Windows command-line quoting and escaping to preserve this guarantee. Rukbat does not guess which strings represent paths. In v0.1, the executable must be a directly launchable binary such as `.exe`. Batch files (`.cmd`, `.bat`) are not supported because their argument-passing rules cannot preserve the one-argument-per-string contract. Executable or environment changes remain the caller's responsibility.

## Runtime path resolution

Standard filesystem operations resolve nonempty relative paths against the captured entry working directory before calling PowerShell or .NET. Absolute paths stay absolute. A root-relative path such as `\data\file.txt` uses the captured directory's drive root. Drive-relative forms such as `C:file.txt` are rejected because they depend on implicit per-drive state; `fs.is_file` and `fs.is_dir` return `false` for them. Invalid/empty operation paths raise Error, except for these existence predicates. This does not change the Error contracts of read, write, list, copy, move, create, or remove.

## Program arguments

`args(): string[]` returns command-line arguments supplied to the entry program in order. It returns `[]` when none were supplied. Argument parsing belongs to the program.
