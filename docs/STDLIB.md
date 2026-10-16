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
import test
import encoding
import math
import datetime
import array
import zip
import hash
import http
import cli
import log
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
| Append text | `fs.append_text` | Encoding is required; no BOM or newline is added. A missing file is created; parents must exist. |
| List matching files | `fs.list_files`, `fs.list_files_recursive` | Only files are returned. `_recursive` includes subdirectories. |
| List directly contained directories | `fs.list_dirs` | Includes empty and Hidden directories, excludes child reparse points, and does not recurse. |
| Check the target kind | `fs.is_file`, `fs.is_dir` | Missing or inaccessible entries return `false`. These checks do not guarantee a later operation will succeed. |
| Read file or directory metadata | `fs.stat` | Returns kind, file byte size, and UTC last-write time. Directory size is `null`; missing/inaccessible entries and final-entry reparse points cause Error. |
| Create a directory and its parents | `fs.create_dir_all` | An existing directory succeeds; a file at that path is an Error. |
| Copy or move one file | `fs.copy_file`, `fs.move_file` | Destination is the complete file path, not a directory. An existing destination is never overwritten; parents must exist. |
| Move or rename one directory tree | `fs.move_dir` | Same-volume rename; destination is the complete directory path, must not exist, and its parent must exist. |
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
Use `fs.stat` for the file and directory metadata specified in the `fs` section below.
The current filesystem API does not provide directory copy or raw byte I/O.
Those operations are not implied by similarly named file functions.

## Admission criteria

Standard operations must be common in Windows automation and repeatedly useful in Rukbat programs.
They must have explicit, statically checked input/output contracts and predictable failure behavior.
They should run on Windows PowerShell 5.1 without additional deployment dependencies.
Use one canonical API per operation. Rukbat's standard library does not aim to provide a comprehensive wrapper for PowerShell APIs.

## Standard output and error

The built-in `print(value)` and `eprint(value)` write to stdout and stderr respectively, with identical type checks and one appended platform newline. Neither requires an import. Explicit string formatting uses `text.from_*`. Output alone does not change the exit code.

## `log`

Use an ordinary Logger value for diagnostic messages. Defaults write INFO and higher
to stderr. Set one field to retain the same records in a file:

```rukbat
import log

logger := log.create()
logger.file = "job.log"
log.info(logger, "Starting")
```

Logging never writes a success/stdout value. ERROR is a severity, not a request to
throw or exit. Only an actual preparation or delivery failure raises Error.

| Signature | Behavior |
|---|---|
| `log.create(): log.Logger` | Return the default configuration without I/O. |
| `log.debug(logger: log.Logger, message: string): void` | Write a DEBUG record to eligible destinations. |
| `log.info(logger: log.Logger, message: string): void` | Write an INFO record to eligible destinations. |
| `log.warn(logger: log.Logger, message: string): void` | Write a WARN record to eligible destinations. |
| `log.error(logger: log.Logger, message: string): void` | Write an ERROR record to eligible destinations. |
| `log.enabled(logger: log.Logger, level: log.Level): bool` | Return whether at least one destination accepts the level. |

All arguments are required and positional. Format non-string messages explicitly
with existing APIs. There is no global Logger, generic write function, alias,
callback, or separate Output/Handler type.

### Logger configuration and levels

`log.Logger` is an ordinary struct with these three fields:

| Field | Type | Default | Meaning |
|---|---|---|---|
| `stderr_level` | `log.Level?` | `log.level_info` | Minimum stderr severity; null disables stderr. |
| `file` | `string?` | `null` | File path; null disables file output. |
| `file_level` | `log.Level` | `log.level_info` | Minimum file severity; ignored while file is null. |

The four read-only constants, from lowest to highest severity, are
`log.level_debug`, `log.level_info`, `log.level_warn`, and `log.level_error`.
A minimum accepts itself and all higher levels. File and stderr thresholds are
independent. Set `stderr_level = null` for file-only logging, or both
`stderr_level = null` and `file = null` to disable output entirely.

Level is opaque and immutable. It supports ordinary storage and non-null same-type
equality, but no ordering, arithmetic, custom numeric levels, public fields,
struct literal, or implicit string/numeric conversion. Nullable Level follows
ordinary null comparison and narrowing. Level and values containing it, including
Logger, cannot cross JSON or PowerShell boundaries; Level cannot be printed.

Logger literals initialize all three fields. The struct owns no stream or external
resource and needs no close operation. Assignment, arguments, returns and containers
use ordinary value copies; mutating a copied Logger does not reconfigure its original.

### Filtering and evaluation

Arguments are evaluated once, left to right, before filtering. In
`log.info(logger, message())`, the Logger is copied before `message()` runs.
Subsequent mutations inside `message()` do not change that call's configuration.
Filtering does not suppress argument side effects or errors. Guard expensive
construction explicitly with `if log.enabled(logger, log.level_debug) { ... }`.

`enabled` checks thresholds only. It does not validate paths or inspect the filesystem,
read the clock, load a native helper, or guarantee a later write. An invalid selected
path can make it return true. When neither destination accepts a writing call,
the call skips message validation, native loading, timestamp capture, path resolution,
formatting, encoding and I/O. A configured but filtered file cannot prevent stderr output.

### Records and destinations

Every record has this fixed field order and ends with exactly one CRLF:

```text
time=2026-10-16T09:00:00.1234567Z level=INFO message="Starting"
```

One UTC wall-clock timestamp is captured per eligible call and shared by both
destinations. Its invariant spelling is `yyyy-MM-ddTHH:mm:ss.fffffffZ`, the same
as `datetime.stringify`. Seven fractional digits do not imply clock precision;
timestamps need not be unique, monotonic, or sorted.

Messages are always quoted JSON strings. Quote and backslash use `\"` and `\\`;
backspace, tab, LF, form feed and CR use `\b`, `\t`, `\n`, `\f` and `\r`.
Other U+0000–U+001F, U+007F–U+009F, U+2028 and U+2029 use lowercase `\uXXXX`.
Other valid Unicode scalars are preserved, including supplementary characters.
Empty messages are valid. Unpaired UTF-16 surrogates raise Error before any output.
Embedded line endings and terminal escape characters cannot create extra physical
records or ANSI control sequences. This format is neither JSON Lines nor a secret
redaction or general sanitization API.

Stderr uses the existing `Console.Error` writer and flushes it after the complete
record. It does not replace that writer, add colors, change host preferences, or
use PowerShell's warning/verbose/error streams. Generated scripts retain their
ordinary UTF-8 console setup and host redirection semantics.

File output uses strict UTF-8 without a BOM. Relative paths use the captured entry
working directory, even after PowerShell/.NET location changes. Use
`path.script_dir()` explicitly for deployment-relative logs. A missing file is
created on its first eligible write; parent directories must already exist.
Existing bytes are not scanned, decoded, rewritten or truncated. An existing log
must be compatible UTF-8 ending at a record boundary; no BOM, newline or tail repair
is added. See [the logging example](../examples/logging.rukbat).

Bytes are prepared before opening the file. Each write uses `FileMode.Append`,
`FileAccess.Write`, and `FileShare.Read`, then calls `Flush(true)` and closes before
returning, as with `fs.append_text`. There is no buffered/background mode, lock retry,
cross-process atomic append, or ordering guarantee. Overlapping writers can fail;
reader sharing must be compatible in both directions. An active `fs.lines` traversal
is not a concurrent log tailer. An I/O failure can leave a partial record. Flush
completion does not guarantee durability across power loss or noncompliant storage.

### Failure behavior

Shared record preparation occurs before delivery. A preparation failure writes
neither destination. Delivery attempts stderr first and then the selected file,
even if stderr failed. File path resolution belongs to the file attempt; an invalid
selected path cannot suppress eligible stderr output. After both attempts, a failure
in either destination raises one Error identifying each failed destination and its
description. File errors include the resolved path, or the supplied path when
resolution failed. A close failure preserves the primary write/flush failure.
Delivery errors do not include the message. Normal source metadata identifies the
logging call site.

Successful delivery to one destination is not rolled back. An Error does not mean
nothing was written; a retry can duplicate a record. Logging inside a catch can
itself fail under ordinary Error semantics. Handle that failure separately when
the original Error must be preserved. The library adds no retry, fallback,
process-exit hook, rotation, history buffer, or retained stream.

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
| `text.length(value: string): int` | `text.length("A😀B")` | `3` |
| `text.slice(value: string, start: int, count: int): string` | `text.slice("A😀B", 1, 1)` | `"😀"` |
| `text.index_of(value: string, search: string): int?` | `text.index_of("A😀B", "B")` | `2` |
| `text.upper(value: string): string` | `text.upper("report.csv")` | `"REPORT.CSV"` |
| `text.lower(value: string): string` | `text.lower("REPORT.CSV")` | `"report.csv"` |
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
API are introduced.

`length`, `slice`, and `index_of` count Unicode code points, starting at zero, rather than
UTF-16 code units or user-perceived grapheme clusters. A supplementary character such as an
emoji counts once; a combining mark counts separately. `slice` takes a start and a count,
not an ending index. Start may equal length only when count is zero. Negative values and
ranges outside the string raise Error without clamping. `index_of` returns the first literal
ordinal match or `null`; an empty search returns `0`. These three operations and `upper`/`lower`
reject unpaired UTF-16 surrogates. Case conversion uses the host .NET invariant Unicode casing
tables, without normalization or full case folding; mappings for newly assigned Unicode
characters can depend on the runtime version. Other text operations retain their existing contracts.

See [the log-filter example](../examples/filter-errors.rukbat) for a procedural file-processing workflow.

### Conversion and parsing

| Signature | Meaning |
|---|---|
| `text.from_int(value: int): string` | Signed decimal representation. |
| `text.from_float(value: float): string` | Culture-independent round-trip representation of a finite binary64 value. |
| `text.from_bool(value: bool): string` | Lowercase `true` or `false`. |
| `text.parse_int(value: string): int` | Parses a signed decimal integer within the signed 64-bit range. |
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

## `array`

All operations return a new array with the input element type, preserving ordinary deep
value-copy semantics. Assign the result to keep the change. Arguments are required and
positional; no explicit type arguments are accepted. `T` and `K` below describe inferred
element types, not user-defined generics. An empty input needs a declared array type.

| Signature | Meaning |
|---|---|
| `array.slice(values: T[], start: int, count: int): T[]` | Copies count elements from the zero-based start index. |
| `array.remove_at(values: T[], index: int): T[]` | Returns a copy without the element at the zero-based index. |
| `array.sort(values: T[]): T[]` | Returns a stable ascending sort of orderable elements. |
| `array.sort_descending(values: T[]): T[]` | Returns a stable descending sort of orderable elements. |
| `array.sort_by(values: T[], keys: K[]): T[]` | Returns values in stable ascending key order. |
| `array.sort_by_descending(values: T[], keys: K[]): T[]` | Returns values in stable descending key order. |

`slice` uses a start and count, not an ending index; it preserves order. Start may equal
length only with count zero. Negative or out-of-range values raise Error without clamping;
`remove_at` rejects all indexes on an empty array. Both work with any supported element type,
including nullable values, nested arrays, structs and opaque standard values.

Orderable elements/keys are non-null `int`, `float`, `string`, `datetime.Timestamp`,
`datetime.Date` and `datetime.Duration`. Other types, including bool, nullable keys, structs
and arrays, are rejected during checking. Strings use ordinal case-sensitive comparison
of UTF-16 code units, independent of culture, without normalization or natural-number sorting.
Integers remain exact, and datetime keys compare their exact tick counts. Equal keys preserve
the original order, including descending sorts. `sort_by` variants require one key per value;
unequal lengths raise Error. Inputs and keys are evaluated once, in written order. Build a
separate key array to order CSV rows or structs without callbacks or new language syntax.

```rukbat
import array
import csv
import text

table := csv.parse_table("name,amount\nHanako,20\nTaro,10\n")
amounts: int[] = []
for row in table.rows {
    amounts.append(text.parse_int(csv.get(row, "amount")))
}
table.rows = array.sort_by_descending(table.rows, amounts)
print(csv.stringify_table(table))
```

## `math`

| Signature | Meaning | Failure |
|---|---|---|
| `math.to_float(value: int): float` | Explicit conversion to binary64, rounded to nearest with ties to even; large integers may lose precision. | None. |
| `math.to_int(value: float): int` | Converts a whole float to signed 64-bit integer without truncating a fraction. | A fractional or out-of-range value causes Error. |
| `math.trunc(value: float): float` | Removes the fractional part toward zero. | None. |
| `math.floor(value: float): float` | Rounds toward negative infinity. | None. |
| `math.ceil(value: float): float` | Rounds toward positive infinity. | None. |

All parameters are required and positional, with no type arguments. These operations do not
introduce implicit conversions. `math.to_int(math.trunc(3.9))` returns `3`;
`math.to_int(3.9)` raises Error. Floats are approximate binary values, not exact decimal money.
Integer JSON and text boundaries retain all 64-bit digits; `json.parse<float>` and
`powershell<float>` explicitly request approximate numeric conversion. A JSON receiver using
binary64 may lose large integer precision; exchange such identifiers as strings when required.

## `datetime`

`datetime.Timestamp`, `datetime.Date`, `datetime.Duration`, and `datetime.Zone` are opaque,
immutable values. They support ordinary variables, parameters, returns, nullable wrappers,
arrays, and struct fields, with no public fields or struct literals.
Named zones such as `datetime.utc`, `datetime.tokyo`, `datetime.eastern`, and
`datetime.pacific_mexico` are read-only `datetime.Zone` constants.
The [time-zone constant catalog](TIME_ZONES.md) defines every supported name and Windows ID.
These constants are independent of the machine's local-zone setting.
Timestamp, Date, and Duration support same-type equality
and ordering; Zone supports equality by its canonical ID. Arithmetic uses the functions below.

Timestamp identifies one UTC instant, independent of presentation. Input offset spelling is
not retained. Date is a Gregorian calendar date without a time zone. Years are 1 through 9999;
timestamp and duration resolution is 100 nanoseconds. Duration stores a signed 64-bit tick count,
with checked construction. Clock resolution and accuracy are properties of the deployment host;
`now` is a wall clock, not a monotonic elapsed-time measurement.

| Signature | Meaning |
|---|---|
| `datetime.now(): datetime.Timestamp` | Reads the wall clock once and returns a UTC instant. |
| `datetime.parse(text: string): datetime.Timestamp` | Parses an offset-qualified timestamp, preserving the instant and up to seven fractional digits. |
| `datetime.stringify(value: datetime.Timestamp): string` | Returns canonical UTC text: yyyy-MM-ddTHH:mm:ss.fffffffZ. |
| `datetime.format(value: datetime.Timestamp, pattern: string, zone: datetime.Zone): string` | Formats an instant using explicit zone rules and the restricted format language. |
| `datetime.parse_local(text: string, pattern: string, zone: datetime.Zone): datetime.Timestamp` | Parses a local clock in an explicit zone; invalid or ambiguous local times cause Error. |
| `datetime.zone(id: string): datetime.Zone` | Looks up a case-sensitive Windows time zone ID, such as Tokyo Standard Time. |
| `datetime.local_zone(): datetime.Zone` | Explicitly obtains a snapshot of the deployment machine's current local zone. |
| `datetime.zone_id(value: datetime.Zone): string` | Returns the Windows zone ID for external configuration. |
| `datetime.create_date(year: int, month: int, day: int): datetime.Date` | Constructs a valid Gregorian date without normalizing invalid components. |
| `datetime.date(value: datetime.Timestamp, zone: datetime.Zone): datetime.Date` | Extracts the calendar date in the specified zone. |
| `datetime.parse_date(text: string): datetime.Date` | Parses exactly yyyy-MM-dd. |
| `datetime.stringify_date(value: datetime.Date): string` | Returns canonical yyyy-MM-dd. |
| `datetime.format_date(value: datetime.Date, pattern: string): string` | Formats a date using date tokens only. |
| `datetime.at(date: datetime.Date, hour: int, minute: int, second: int, zone: datetime.Zone): datetime.Timestamp` | Constructs a local clock with zero fractional seconds and resolves it in the explicit zone. |
| `datetime.add_days(value: datetime.Date, count: int): datetime.Date` | Adds signed calendar days. |
| `datetime.add_months(value: datetime.Date, count: int): datetime.Date` | Adds signed calendar months; a missing day is clamped to the destination month's last day. |
| `datetime.milliseconds(count: int): datetime.Duration` | Constructs a signed elapsed time from whole milliseconds. |
| `datetime.seconds(count: int): datetime.Duration` | Constructs a signed elapsed time from whole seconds. |
| `datetime.minutes(count: int): datetime.Duration` | Constructs a signed elapsed time from whole minutes. |
| `datetime.hours(count: int): datetime.Duration` | Constructs a signed elapsed time from whole hours. |
| `datetime.days(count: int): datetime.Duration` | Constructs a signed elapsed time; each day is exactly 24 hours. |
| `datetime.add(value: datetime.Timestamp, duration: datetime.Duration): datetime.Timestamp` | Adds elapsed time on the UTC timeline. |
| `datetime.between(start: datetime.Timestamp, end: datetime.Timestamp): datetime.Duration` | Returns end minus start without losing precision. |
| `datetime.total_seconds(value: datetime.Duration): float` | Returns approximate binary64 seconds for display or aggregation; use Duration comparison for exact decisions. |
| `datetime.from_unix_seconds(value: int): datetime.Timestamp` | Constructs an instant from signed whole seconds since 1970-01-01T00:00:00Z. |
| `datetime.from_unix_milliseconds(value: int): datetime.Timestamp` | Constructs an instant from signed whole milliseconds since the Unix epoch. |
| `datetime.to_unix_seconds(value: datetime.Timestamp): int` | Returns signed epoch seconds, flooring fractional seconds, including before the epoch. |
| `datetime.to_unix_milliseconds(value: datetime.Timestamp): int` | Returns signed epoch milliseconds, flooring sub-millisecond fractions, including before the epoch. |

All parameters are required and positional; no type arguments or implicit conversions are
accepted. Invalid input, unknown zones, range overflow, nonexistent local times, and ambiguous
local times raise ordinary Error. No values are silently wrapped, date-range-clamped or replaced
with the current date. The documented month-end correction in `add_months` is intentional;
adding then subtracting a month need not restore the same day.

`parse` accepts exactly `YYYY-MM-DDTHH:mm:ss[.fraction](Z|+HH:mm|-HH:mm)`, with ASCII digits,
uppercase T/Z, one through seven fractional digits, and offsets up to plus/minus 14 hours.
Whitespace, missing offsets, leap seconds (`:60`), `24:00`, excessive precision, and the unknown
offset `-00:00` are rejected. `stringify` always emits seven fractional digits and Z; parse of
that output restores the exact instant. `parse_date` accepts neither a time nor whitespace.

Format tokens are `yyyy`, `MM`, `dd`, `HH`, `mm`, `ss`, one through seven `f` characters, and
`zzz` for a numeric offset. Other letters must be single- or double-quoted literal text.
Separators are literal, and all numeric output is culture-independent. Unknown tokens,
unclosed quoted text, and an empty pattern cause Error; .NET standard format names are not
accepted. `format_date` rejects time/offset tokens. `parse_local` rejects offset tokens and
requires yyyy, MM, dd, HH, and mm exactly once; omitted seconds are zero and fractions require ss.
Local parsing rejects repeated fields. A
chosen display pattern may omit precision; use `stringify` for exact timestamp serialization.

A Zone contains Windows time-zone rules, including daylight-saving transitions. An offset in
an input timestamp is not a Zone. `zone` does not resolve IANA aliases or infer the machine zone.
Only `local_zone` selects a zone from the machine's local-zone setting. Named constants and
`zone(id)` use the deployment host's installed Windows rules, including daylight saving.
Constants are resolved only when accessed; if the selected ID is absent on that host,
access raises ordinary Error. Unused constants do not require their IDs to be installed.
The constant catalog is fixed by Rukbat, not generated from the compiler host's zones.
Use `zone(id)` for external configuration or IDs introduced after the catalog.
Local times that occur twice
or never during a daylight-saving transition raise Error; use offset-qualified `parse` to select
one occurrence. `days(1)` is 24 elapsed hours; `add_days(date, 1)` is the next calendar date.

Direct print/eprint, JSON conversion, and PowerShell arguments/results containing datetime
values are rejected, including nested arrays/structs. Use the explicit string, zone ID, Unix int,
or approximate duration conversion at the boundary. Generated scripts embed the helper and
run on Windows PowerShell 5.1 without a separate Rukbat installation.

```rukbat
import datetime

tokyo := datetime.tokyo
now := datetime.now()
print(datetime.stringify(now))
print(datetime.format(now, "yyyy-MM-dd HH:mm:ss zzz", tokyo))
deadline := datetime.parse("2026-10-31T18:00:00+09:00")
if now >= deadline {
    print("expired")
}
today := datetime.date(now, tokyo)
yesterday := datetime.add_days(today, -1)
print(datetime.stringify_date(yesterday))
```

## `encoding`

`encoding.Encoding` is a validated type with 26 read-only predefined values:

| Value | External name | Meaning |
|---|---|---|
| `encoding.utf8` | `utf-8` | UTF-8 |
| `encoding.utf16_le` | `utf-16le` | UTF-16 little-endian |
| `encoding.utf16_be` | `utf-16be` | UTF-16 big-endian |
| `encoding.windows_932` | `windows-932` | Windows CP932, including NEC/IBM extensions |
| `encoding.utf32_le` | `utf-32le` | UTF-32 little-endian |
| `encoding.utf32_be` | `utf-32be` | UTF-32 big-endian |
| `encoding.ascii` | `ascii` | Strict ASCII |
| `encoding.iso_8859_1` | `iso-8859-1` | ISO-8859-1 |
| `encoding.windows_874` | `windows-874` | Windows code page 874 |
| `encoding.windows_1250` | `windows-1250` | Windows code page 1250 |
| `encoding.windows_1251` | `windows-1251` | Windows code page 1251 |
| `encoding.windows_1252` | `windows-1252` | Windows code page 1252 |
| `encoding.windows_1253` | `windows-1253` | Windows code page 1253 |
| `encoding.windows_1254` | `windows-1254` | Windows code page 1254 |
| `encoding.windows_1255` | `windows-1255` | Windows code page 1255 |
| `encoding.windows_1256` | `windows-1256` | Windows code page 1256 |
| `encoding.windows_1257` | `windows-1257` | Windows code page 1257 |
| `encoding.windows_1258` | `windows-1258` | Windows code page 1258 |
| `encoding.windows_936` | `windows-936` | Windows code page 936 |
| `encoding.windows_949` | `windows-949` | Windows code page 949 |
| `encoding.windows_950` | `windows-950` | Windows code page 950 |
| `encoding.windows_437` | `windows-437` | Windows code page 437 |
| `encoding.windows_850` | `windows-850` | Windows code page 850 |
| `encoding.windows_852` | `windows-852` | Windows code page 852 |
| `encoding.windows_866` | `windows-866` | Windows code page 866 |
| `encoding.gb18030` | `gb18030` | GB18030 (Windows code page 54936) |

| Signature | Meaning | Failure |
|---|---|---|
| `encoding.parse(name: string): encoding.Encoding` | Converts an external encoding name to a typed value. Names are case-sensitive: predefined names above or canonical `windows-N` for supported additional code pages. No aliases, whitespace trimming, or detection. | An unknown, unavailable, or unsupported name causes Error. |
| `encoding.from_code_page(number: int): encoding.Encoding` | Validates a fixed Windows code page on the target and returns its canonical value; 932 equals `encoding.windows_932`. | An unavailable or unsupported strict converter causes Error. |
| `encoding.name(value: encoding.Encoding): string` | Returns the canonical external name for configuration or logging. | None. |

Predefined values are not the support ceiling. `from_code_page` supports additional fixed Windows code pages when the target Windows APIs accept strict decoding and encoding flags. Additional values use `windows-N`; predefined special names remain canonical (`65001` is `utf-8`, not `windows-65001`). `parse(name(value))` preserves identity.

Numbers 0, 1, 2, and 3 (environment-dependent defaults), Symbol (42), UTF-7, stateful encodings such as ISO-2022 and ISCII, and EUC-JP (20932 / 51932) are unsupported. EUC-JP and ISO-2022-JP need separate mapping and strict validation policies. Unsupported values cause ordinary Error; no converter silently substitutes characters or applies best-fit transliteration. Availability is checked on the deployment machine.

Use the typed values directly in source. Use `parse` only at an external text boundary:

```rukbat
import encoding
import fs

selected := encoding.windows_932
content := fs.read_text("input.csv", selected)
print(encoding.name(selected))
```

Values support ordinary assignment, parameters, returns, arrays, struct fields, nullable wrappers,
and same-type `==` / `!=`. They have no fields, constructors, numeric conversions, or implicit
conversion to/from strings. To print or serialize an encoding, call `encoding.name`. To restore
it, call `encoding.parse`. Direct JSON conversion and direct PowerShell arguments/results,
including nested containers, are rejected. This module adds no user-declared enum syntax.

## `fs`

The module contains both file and directory operations. Import `fs` once for either kind.

### File operations

`fs.Info` is an ordinary value struct with `is_dir: bool`, `size: int?`, and
`modified: datetime.Timestamp` (UTC last-write time). `fs.stat` reads a metadata snapshot;
files have a byte size and directories have `null`. Import `datetime` to compare or format
the timestamp. Missing/inaccessible entries and final-entry reparse points (including links
and junctions) raise Error. Parent paths retain normal filesystem resolution. Metadata can
change during or after the call; this is not an atomic snapshot, file lock, identity check,
or guarantee about a subsequent operation. Timestamp precision depends on the filesystem.
Because `modified` is a `datetime.Timestamp`, `fs.Info` cannot be converted directly to/from JSON. To export metadata, copy the fields into a separate struct and convert `modified` with `datetime.stringify`.

| Signature | Meaning | Failure |
|---|---|---|
| `fs.stat(path: string): fs.Info` | Reads file or directory kind, byte size, and UTC last-write time. | Missing/inaccessible entries, reparse points, and I/O failures cause Error. |
| `fs.list_files(directory: string, pattern: string): string[]` | Returns absolute paths of directly contained, non-Hidden files that match the file-system wildcard `pattern`. It excludes directories, does not recurse, and does not guarantee order. | An inaccessible or nonexistent directory, or an invalid pattern, causes Error. |
| `fs.list_files_recursive(directory: string, pattern: string): string[]` | Returns absolute paths to files matching the filename wildcard `pattern` at every depth beneath `directory`, including direct children. It excludes directories and does not guarantee order. | An inaccessible/nonexistent directory, a file passed as `directory`, an invalid path or pattern, or a traversal failure causes Error. |
| `fs.read_text(path: string, encoding: encoding.Encoding): string` | Reads text with one explicit encoding value. | Missing/inaccessible files, an invalid byte sequence, or a conflicting UTF-16 BOM causes Error. |
| `fs.lines(path: string, encoding: encoding.Encoding): fs.Lines` | Creates an immutable descriptor for sequential physical lines. Each for opens an independent traversal and closes it on termination. | Invalid paths or encodings cause Error during descriptor creation. Opening, reading, decoding, BOM conflicts, sharing conflicts, and cleanup failures cause Error during traversal. |
| `fs.write_text(path: string, text: string, encoding: encoding.Encoding): void` | Replaces a file with one explicit encoding value, without a BOM or implicit newline. It does not create parent directories. Complete new contents are written to a temporary file in the destination directory before it is moved or used to replace the destination. | An inaccessible path, missing parent directory, or unencodable text causes Error. |
| `fs.append_text(path: string, text: string, encoding: encoding.Encoding): void` | Appends strictly encoded text bytes to a file, without a BOM or implicit newline. Creates a missing file, including for empty text, and does not create parent directories. | An invalid/inaccessible path, missing parent, unencodable text, sharing conflict, or I/O failure causes Error. An I/O failure can leave a partial append. |
| `fs.create_temp_file(directory: string): string` | Exclusively creates an empty temporary file in an existing directory and returns its absolute path. | An invalid/missing/inaccessible parent, a file passed as parent, exhausted name collisions, or an I/O failure causes Error. |
| `fs.is_file(path: string): bool` | Returns `true` for an existing file. Missing files, directories, invalid paths, and access failures or other cases that File.Exists cannot determine return `false`. | This predicate returns `false` rather than raising Error when path resolution or existence checking fails. |
| `fs.copy_file(source: string, destination: string): void` | Copies one file without overwriting an existing destination or creating its parent directory. | A missing, inaccessible, or directory source; an existing destination; a missing parent; identical source and destination; or an I/O failure causes Error. |
| `fs.move_file(source: string, destination: string): void` | Moves one file. Moving within one directory renames it. It does not overwrite an existing destination or create its parent directory. On success the source no longer exists. | A missing or directory source; an existing destination; a missing parent; identical source and destination; or an I/O failure causes Error. Cross-volume moves are allowed but are not atomic; after an error, the destination may remain. |
| `fs.remove_file(path: string): void` | Deletes one file. Removing a missing file succeeds. | A directory, readonly or in-use file, invalid path, or I/O failure causes Error. A file reparse point removes the entry, not its target. |

The encoding argument has type `encoding.Encoding`; strings are rejected during checking. Import `encoding` and pass a predefined value or a validated `encoding.from_code_page` / `encoding.parse` result. `encoding.utf8` accepts and removes only a leading UTF-8 BOM on read. UTF-16 and UTF-32 values accept and remove their matching leading BOM, accept no BOM, and reject the opposite-endian BOM. All writes are strict and BOM-free. `encoding.windows_932` means Windows code page 932 (CP932 / Windows Shift-JIS), for text commonly used by legacy Japanese Windows software. It is deliberately CP932 rather than a claim to implement every Shift_JIS variant, and it has no BOM handling. Every encoding rejects invalid input and unencodable output rather than replacing data.

Relative paths for `fs` are resolved from the filesystem working directory captured at entry-script startup, not the script directory or a later PowerShell location. `fs.write_text` first validates and encodes its complete contents to bytes, then writes and flushes those bytes to a uniquely named temporary file in the destination directory. It then uses `File.Replace` for an existing destination or `File.Move` for a missing one. Failures before that final filesystem operation leave an existing destination unchanged. A concurrent creator that wins the missing-destination race is replaced through `File.Replace`, not deleted. Rukbat does not guarantee durability against process, system, storage, or filesystem failure during the final operation, nor does it guarantee atomic behavior on every filesystem. `File.Replace` can merge destination metadata such as attributes and ACLs and can fail while doing so; `fs.write_text` makes no metadata or ACL preservation guarantee.

`fs.list_files` and `fs.list_files_recursive` return absolute paths. `list_files` considers only direct children; `list_files_recursive` considers every depth. Both treat `directory` as a literal path. Their nonempty `pattern` applies only to each filename, using the same file-system filter behavior; the supported simple forms include `*` and `?`. It cannot contain `/` or `\\`, so it cannot express directory hierarchy globs. `list_files` excludes Hidden files. `list_files_recursive` excludes Hidden files and directories, including hidden subtrees. System attributes do not exclude entries. A directory reparse point explicitly supplied as the `list_files_recursive` root is a valid traversal root. ReparsePoint entries discovered beneath that root are excluded, so that operation neither returns nor recurses into child symbolic links or junctions. Enumeration returns no partial result when a platform error occurs.

`encoding.windows_932` reads use the Windows code-page mapping with strict validation, including NEC-selected IBM and IBM extension byte sequences for the same character (for example, `EE E0` and `FB FC` both decode to U+9AD9). A valid read does not imply that re-encoding reproduces the original bytes. The generated script embeds its decoder and requires no additional installation.

`fs.append_text` validates and encodes the supplied text before opening or creating the file.
Existing bytes, including a BOM, remain unchanged; their encoding is not detected or validated.
The caller must select an encoding compatible with the existing contents. The operation opens
one append stream, permits readers and excludes other writers while that stream is open, then
writes and flushes only the new bytes. Sharing conflicts cause Error rather than waiting or
retrying. It is not a transactional replacement: write/flush failures or host termination may
leave a partial suffix. Empty text creates a missing empty file and adds nothing to an existing
file. Neither a BOM nor a newline is added; put the desired line ending in the text explicitly.

### Sequential line reads

`fs.Lines` stores an absolute path resolved against the captured entry working directory and
a canonical encoding name. Creating or copying it does not open or check the target file.
Variables, const values, parameters, returns, fields, arrays, dictionaries, and nullable values
can retain descriptors. They expose no fields, constructor, index, length, next, or close.
Printing, equality, JSON conversion, and PowerShell transfer are rejected, including within containers.

Use `for line in fs.lines(path, encoding)` to read strings. Each traversal evaluates its input once,
opens a separate read-only handle with FileShare.Read, and starts at the beginning. Repeated or
nested traversal is independent. Reassigning the descriptor or loop variable does not move an
active reader. A descriptor does not freeze file contents or file identity; a later traversal opens
the file then present at its stored path. Writers and deletion/replacement can conflict with the
open handle. This is synchronous reading of regular files, not log tailing.

CRLF, lone CR, and lone LF delimit lines and are removed. Empty input produces no lines;
an empty physical line produces an empty string. A final terminator adds no extra empty line.
Other characters, including NUL and Unicode separators, are preserved. All accepted encoding
values and existing BOM rules apply. For valid, unchanged input, complete traversal matches
`text.split_lines(fs.read_text(path, encoding))`.

EOF, break, return, and escaping Error release the handle before control continues. Continue
and errors handled inside the body keep it open. A valid exit or forced host termination does
not guarantee generated cleanup. Return expressions are evaluated and copied before cleanup,
but their result is emitted only after cleanup succeeds. A cleanup failure becomes Error;
with a primary Error it preserves that Error's location/message and appends cleanup information.

Reading can prefetch bytes: an error after a valid line may occur before that line reaches the
body. The number of processed lines for invalid input is unspecified. Close does not decode
or validate unread input. Internal retention is proportional to a fixed buffer plus the longest
line encountered, excluding user-retained values. There is no line-length limit or fixed memory
bound for arbitrarily long lines. Multiline CSV records require a record parser rather than
independent calls to csv.parse on physical lines.

### Directory operations

| Signature | Meaning | Failure |
|---|---|---|
| `fs.list_dirs(directory: string, pattern: string): string[]` | Returns absolute paths of directly contained ordinary directories matching a directory-name wildcard, including empty, Hidden and System directories. Does not recurse; child reparse points are excluded; order is unspecified. | An invalid/inaccessible/nonexistent root, a file passed as root, an invalid pattern, or an enumeration failure causes Error. |
| `fs.move_dir(source: string, destination: string): void` | Moves an ordinary directory and its contents by a same-volume rename. Within one parent this renames the directory. Destination must not exist and its parent must exist. | A missing/file/reparse-point source, existing destination, missing parent, identical paths, destination beneath source, different drives/UNC shares or volumes, protected source, or I/O failure causes Error. |
| `fs.create_dir_all(path: string): void` | Creates the directory and any missing parent directories. Creating an existing directory succeeds. | A file at the target path, an invalid path, or an I/O failure causes Error. |
| `fs.create_temp_dir(directory: string): string` | Exclusively creates an empty temporary directory in an existing directory and returns its absolute path. | An invalid/missing/inaccessible parent, a file passed as parent, exhausted name collisions, or an I/O failure causes Error. |
| `fs.is_dir(path: string): bool` | Returns `true` only for an existing reachable directory. Files, missing paths, invalid paths, inaccessible paths, and indeterminate checks return `false`. | This predicate returns `false` rather than raising Error. |
| `fs.remove_dir(path: string): void` | Deletes an empty directory. Removing a missing directory succeeds. | A file, a non-empty directory, a filesystem root, the captured working directory or its ancestor, or an I/O failure causes Error. A directory reparse point removes the entry, not its target. |
| `fs.remove_dir_all(path: string): void` | Deletes a directory and its ordinary subtree. Removing a missing directory succeeds. | A file, a filesystem root, the captured working directory or its ancestor, or an I/O failure causes Error. Partial deletion is possible. Reparse points at the target or beneath it are removed as entries and are never traversed. |

`fs.list_dirs` treats its root literally and resolves relative roots against the captured entry
working directory. Its nonempty pattern applies to directory names using the same file-system
wildcard filter as `list_files`; `*` and `?` are supported, and `/` or `\` in the pattern is
rejected. It returns no partial result on error. Unlike existing file enumeration, it includes
Hidden directories. An explicitly supplied root may be a directory reparse point; only child
reparse points are excluded. Use repeated calls for explicit traversal.

`fs.move_dir` preserves the directory tree, including empty/Hidden children, by moving the
directory entry rather than copying and deleting its contents. Child links move with the tree
without being traversed; a reparse point cannot be the source itself. The source cannot be a
filesystem root, the captured entry working directory, or an ancestor of that directory.
Both paths are resolved against the captured directory, independent of later location changes.
Trailing separators do not distinguish paths, and ordinal case-insensitive same-path checks
also reject case-only renames. Moving into a descendant is rejected. Cross-volume moves have
no copy/delete fallback. Parent paths use normal filesystem resolution; this is not a path
containment or hostile-concurrency guarantee. The operation does not create parent directories,
replace an existing destination, or provide a recovery journal.

```rukbat
import fs
import path
import encoding

fs.create_dir_all("processed")
for directory in fs.list_dirs("incoming", "batch-*") {
    destination := path.resolve(path.resolve(path.working_dir(), "processed"), path.filename(directory))
    fs.move_dir(directory, destination)
    fs.append_text("moves.log", "Moved " + destination + "\r\n", encoding.utf8)
}
```

### Temporary files and directories

`fs.create_temp_file` and `fs.create_temp_dir` create entries before returning; they do not
just propose unused names. Each candidate is created exclusively. An existing file or
directory is never reused, truncated, or removed, including when another caller wins the
creation race. Name collisions are retried with new random candidates, with at most 32
attempts per call. Other filesystem errors fail without retrying as name collisions.

The parent must already exist. It is a literal path; relative parents use the captured
working directory, just like other `fs` operations. Choose the parent explicitly; neither
operation selects the operating system's temporary directory or creates parent directories.
Parent paths use normal filesystem resolution, including directory reparse points. These
operations do not lock the parent or protect returned entries from subsequent changes by
other processes. Names and extensions are implementation details; callers should use the
returned path directly. A created file has zero bytes and no open handle on return, and a
created directory has no children. Windows permissions are inherited normally from the parent.

Cleanup is explicit. Call `fs.remove_file`, `fs.remove_dir`, or `fs.remove_dir_all` when done;
there is no automatic removal on return, Error, or process termination. If a later operation
fails, use `try` / `catch` when cleanup is needed on that path too.

```rukbat
import fs
import encoding

fs.create_dir_all("work")
directory := fs.create_temp_dir("work")
try {
    file := fs.create_temp_file(directory)
    fs.write_text(file, "temporary content", encoding.utf8)
    print(fs.read_text(file, encoding.utf8))
    fs.remove_file(file)
} catch error {
    fs.remove_dir_all(directory)
    throw error
}
fs.remove_dir(directory)
```

### Recoverable file moves

| Signature | Meaning |
|---|---|
| `fs.move_files_batch(sources: string[], destinations: string[], journal: string): void` | Executes a move plan with a persistent recovery journal, under the restrictions below. |
| `fs.recover_file_moves(journal: string): void` | Recovers or verifies the recorded move plan, under the restrictions below. |

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
| `path.script_dir(): string` | Returns the deployed script directory captured at startup. | None. |
| `path.working_dir(): string` | Returns the absolute filesystem working directory captured at entry-script startup. | None. |
| `path.resolve(base_dir: string, value: string): string` | Resolves a path against an explicit absolute directory and returns a normalized absolute Windows path. No entry needs to exist. | A non-absolute base, invalid/empty path, drive-relative path, or unsupported path form causes Error. |
| `path.relative(base_dir: string, target: string): string` | Returns the component-relative path from an absolute directory to an absolute target, or `"."` for the same location. No entry needs to exist. | Non-absolute or invalid inputs, unsupported path forms, or different drives/UNC shares cause Error. |
| `path.is_absolute(value: string): bool` | Tests whether a supported Windows path fixes its drive or UNC server/share independently of any current location. | Empty, invalid, relative, and unsupported path forms return `false`. |
| `path.stem(value: string): string` | Returns the final filename without its last extension. Roots, trailing separators, and final `.`/`..` components return `""`. | Invalid/empty paths or unsupported path forms cause Error. |
| `path.change_extension(value: string, extension: string): string` | Returns a path with its final extension replaced; `""` removes the extension. Preserves directory spelling and does not change a file. | An invalid path or extension, unsupported path form, absent filename, or an empty resulting filename causes Error. |
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

`working_dir` returns the same captured entry directory used by relative `fs` paths and
the default child-process working directory. Later PowerShell or .NET directory changes
do not change this value, including calls from imported functions.

`resolve` requires a fully qualified absolute `base_dir`, interpreted as a directory even
when it does not exist. Ordinary relative `value` paths resolve against that directory;
a root-relative value such as `/data/file.csv` resolves from the base's drive or UNC share
root. A fully qualified value uses its own root. `.` and `..` follow Windows full-path
normalization; separators become `\`, and a drive/UNC share root retains its trailing `\`.
Other trailing separators are preserved. It does not resolve links, check existence,
expand environment variables or `~`, or interpret wildcard patterns.

```rukbat
input := path.resolve(path.working_dir(), "input/data.csv")
config := path.resolve(path.script_dir(), "config/settings.json")
data := path.resolve(path.parent(config), "../data")
```

`relative` requires both arguments to be fully qualified absolute paths; use `resolve`
first for relative input. It normalizes both paths and compares roots and components
with Windows ordinal case-insensitive comparison. The base is always a directory.
It ignores trailing separators, returns `"."` for the same components, and uses `\`
separators and `..` for parent traversal. For example,
`relative("C:/work/data", "C:/work/out/a.csv")` returns `"..\\out\\a.csv"`.
Different drives, servers, or UNC shares raise Error rather than returning an absolute
path. These are path-value operations, not filesystem identity or containment checks.

`is_absolute` accepts drive-absolute paths such as `C:/work` and complete UNC paths such
as `//server/share/data`. Ordinary relative paths, root-relative `/data`, drive-relative
`C:data`, incomplete UNC roots, and unsupported forms return `false`. A `true` result
does not imply existence, permissions, or that a later filesystem operation will succeed.

The new `resolve`, `relative`, `is_absolute`, `stem`, and `change_extension` operations
support ordinary Windows drive and UNC paths. Device namespaces (`\\?\` and `\\.\`),
PowerShell provider paths, alternate data streams, drive-relative spellings, and invalid
path characters are rejected. The transformations raise Error; `is_absolute` returns
`false`. Whitespace-only and empty path inputs are invalid. Existing lexical operations
retain their own contracts, including `parent("C:child")`.

`stem` and `change_extension` follow the existing last-extension rule:
`stem("archive.tar.gz")` is `"archive.tar"`, `stem("README")` is `"README"`, and
`stem(".env")` is `""` because `extension(".env")` is `".env"`.
`stem("report.")` is `"report"`; extension replacement/removal also removes that final dot.
Extension changes preserve the original directory text, separators, and relative/absolute
form. The extension must be empty or start with `.` and contain at least one more character;
separators, colons, control characters, and invalid path characters are forbidden.
`change_extension("dir/report.csv", ".json")` is `"dir/report.json"`;
`change_extension("dir/report.csv", "")` is `"dir/report"`.
The operation rejects roots, trailing separators, final `.`/`..`, and removals such as
`change_extension(".env", "")` that would leave no filename.

`equal` compares resolved path spelling, not filesystem object identity. It does not resolve symbolic links, junctions, hard links, short names, or filesystem object IDs.

Existing `equal` and `find_duplicate` preserve trailing separators in their comparison;
`equal("C:/work", "C:/work/")` is `false`. In contrast, `relative` compares directory
components and returns `"."` for that pair.

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
import encoding
import fs

people := csv.parse_table(fs.read_text("people.csv", encoding.windows_932))
kept: csv.Row[] = []

for person in people.rows {
    if csv.get(person, "enabled") == "true" {
        kept.append(csv.set(person, "note", "checked"))
    }
}
people.rows = kept
people.rows.append(csv.row(people.columns, ["Hanako", "true", "new"]))
fs.write_text("enabled.csv", csv.stringify_table(people), encoding.utf8)
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

The separator is comma. CRLF and LF separate records; a final separator is optional. Quoted fields preserve commas and newlines, and `""` inside a quoted field becomes `"`. Whitespace and Unicode are preserved. Empty fields, including quoted empty fields, return `""`. An empty input returns `[]`; a blank record returns `[""]` and is not discarded. A bare CR outside a quoted field, an unquoted quote, an unclosed quote, or non-separator content after a closing quote causes Error. `csv.parse` accepts a Rukbat string and does not decode text encodings or treat U+FEFF specially; select the file encoding with `fs.read_text`, for example `csv.parse(fs.read_text("legacy.csv", encoding.windows_932))`.

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
import encoding
import fs

rows: string[][] = [
    ["name", "note"],
    ["Taro", "contains, a comma"],
    ["Hanako", ""]
]
fs.write_text("people.csv", csv.stringify(rows), encoding.utf8)

loaded := csv.parse(fs.read_text("people.csv", encoding.utf8))
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
import encoding
import fs

document := json.parse(fs.read_text("response.json", encoding.utf8))
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
an exact 64-bit integer, and conversion to `float` still requires a finite binary64 value.
`stringify` preserves numeric spelling, emits object properties in ordinal name order, and preserves
array order. Whitespace and the original spelling of string escapes are not preserved.

`items` and `keys` return ordinary independent Rukbat arrays. Updating those arrays does not change
the original JSON document. Convert a selected object with `as<MyStruct>` when a small part of a
large document has a useful known structure. Known structs may also declare a `json.Value` field
to retain an arbitrary subtree.

### Updating JSON while retaining unknown data

Every update returns a new `json.Value`. Assign that result explicitly. Object names use the same
literal, case-sensitive rules as `get`. `set_at` and `remove_at` require an existing element;
use `append` to add at the end. Replacements may be `json.Value`, primitives, supported structs,
arrays or string-keyed dictionaries, nullable values, or literal `null`. Replacement types are inferred; updates do not accept
`<T>`. Empty replacement arrays need explicitly typed variables. `append` adds an array replacement
as one element, without flattening it.

Update a nested child first, then assign it back to its parent. Unknown siblings and untouched
subtrees retain their data, including original JSON number tokens. Updating a child alone does
not change its parent. Replacing an entire child intentionally replaces all its contents.

```rukbat
import json
import encoding
import fs

config := json.parse(fs.read_text("config.json", encoding.utf8))
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
fs.write_text("updated.json", json.stringify(config), encoding.utf8)
```

Use schema-free parsing for updates that must retain unknown properties. Converting an object to
a struct with `parse<T>` or `as<T>` ignores undeclared properties; stringifying that struct cannot
restore them. Serialization emits compact text and sorted keys for dynamic objects, so original
whitespace, key order, and escape spelling are not retained.

### Dictionaries and JSON objects

Use a dictionary for typed in-memory lookup and mutation; JSON conversion is an explicit data
boundary. `json.stringify(d)` encodes a supported `dict[string, V]` as an object with keys
sorted by ordinal order. `json.parse<dict[string, V]>(text)` and `json.as<dict[string, V]>(value)`
require an object, strictly convert every property value, and insert keys in ordinal order.
JSON updates accept supported dictionaries as replacements. Null/numeric/duplicate-property rules
remain unchanged. A round trip preserves contents, but canonicalizes insertion order.

Integer-keyed dictionaries are rejected recursively, including inside arrays and structs; keys
are never implicitly converted to strings. Opaque-valued dictionaries such as `dict[string, fs.Info]`
are legal in memory but retain the existing JSON exclusions. To export integer keys, build an
array of explicitly named entry structs.

```rukbat
import json

ports := {"https": 443, "http": 80}
print(json.stringify(ports))
loaded := json.parse<dict[string, int]>('{"http":80,"https":443}')
print(loaded["https"])
```

### Reading a known type

`T` may be `int`, `float`, `string`, `bool`, `json.Value`, a struct, an array of supported values,
`dict[string, V]` with recursively supported V, or
a nullable form of a supported type. `csv.Row`, `cli.Result`, `encoding.Encoding`, `hash.Algorithm`,
all `datetime` types, `http.Method`, and `http.Body` are not supported. This restriction also applies
when these types occur in arrays, nullable values, dictionary values, or struct fields, including `fs.Info.modified`.
The same restriction applies to values passed to `stringify` and the JSON update operations.
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
import encoding
import fs

struct Config {
    name: string
    enabled: bool
}

config := Config {
    name: "nightly",
    enabled: true
}
fs.write_text("config.json", json.stringify(config), encoding.utf8)

loaded := json.parse<Config>(fs.read_text("config.json", encoding.utf8))
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

## `hash`

`hash.Algorithm` is an opaque validated value type. Its read-only members are `hash.sha256`,
`hash.sha384`, `hash.sha512`, `hash.sha1`, and `hash.md5`. Use SHA-256 for ordinary artifact
checksums. SHA-1 and MD5 are provided for matching legacy checksums, not new tamper detection.
Algorithm values support assignment, ordinary containers, parameters, returns, and same-type
`==` / `!=`. They have no constructors, fields, ordering, implicit conversions, or direct
printing/JSON/PowerShell boundaries. These restrictions apply recursively to containers.

| Signature | Meaning | Failure |
|---|---|---|
| `hash.file(path: string, algorithm: hash.Algorithm): string` | Hashes the file's exact bytes by streaming, returning lowercase hexadecimal. Names and metadata are excluded. | Missing/unreadable files, sharing conflicts, or unavailable algorithms cause Error. |
| `hash.text(value: string, algorithm: hash.Algorithm, encoding: encoding.Encoding): string` | Strictly encodes text without BOM or added newline and returns its lowercase hexadecimal digest. | Invalid UTF-16, unencodable text or unavailable algorithms cause Error. |
| `hash.parse(name: string): hash.Algorithm` | Restores one exact lowercase external name: sha256, sha384, sha512, sha1, or md5. | Unknown names, aliases, case changes and surrounding whitespace cause Error. |
| `hash.name(algorithm: hash.Algorithm): string` | Returns the canonical external name. | None. |

The file handle allows concurrent readers but denies concurrent writes/deletion while hashing.
A digest comparison does not authenticate the origin of the expected digest.

## `zip`

The module exports ordinary value structs:

```rukbat
struct Options {
    name_encoding: encoding.Encoding
    max_entries: int
    max_total_bytes: int
}

struct Entry {
    name: string
    is_directory: bool
    size: int
    compressed_size: int
}
```

| Signature | Meaning | Failure |
|---|---|---|
| `zip.create(source_dir: string, archive_path: string): void` | Creates a UTF-8 Deflate ZIP containing the source directory's contents, including hidden files and empty directories. The source directory itself is not included as an extra root. | Invalid paths, an existing output, missing parent, links, unreadable files, or an output inside the source cause Error. |
| `zip.options(name_encoding: encoding.Encoding): zip.Options` | Creates reading/extraction options with max_entries 100000 and max_total_bytes 10737418240 (10 GiB). The encoding applies only to names without the ZIP UTF-8 flag. | UTF-16/32 name encodings or an unsupported strict converter cause Error. |
| `zip.list(archive_path: string, options: zip.Options): zip.Entry[]` | Lists validated central metadata in archive order. Entry names use /, directory names end in /, sizes are bytes, and directory size is zero. | Malformed/unsafe/unsupported metadata, name collisions or declared limits cause Error. This does not read and verify every payload's CRC. |
| `zip.extract(archive_path: string, destination_dir: string, options: zip.Options): void` | Extracts into a new directory after validating every entry name and verifies actual size and CRC while streaming. | An existing destination (including an empty directory), missing parent, invalid metadata, damaged data, limits, links or I/O failure cause Error. |

All arguments are required and positional. Relative filesystem paths resolve against the captured
entry working directory. Reading supports Store, Deflate, and ZIP64; encrypted, split and other
compression formats are rejected. New archives use UTF-8 names; reads strictly respect each entry's
UTF-8 flag, otherwise the explicit name_encoding is used, with no automatic detection or replacement.
The Unicode Path extra field is not interpreted; names follow this explicit flag/encoding rule.
File contents are never transcoded. UTF-16 and UTF-32 are not ZIP name encodings.

Both list and extract revalidate edited options. max_entries is 1 through 2147483647; max_total_bytes
is a positive signed 64-bit byte count. Total declared uncompressed size is checked before creating
an extraction directory, and actual sizes are checked while reading. Central metadata is capped at
64 MiB before a library parses/allocates its entry collection; directory nesting is capped at 256
components. The decoded path index also has a 64 MiB budget, accounting for UTF-16 key bytes plus
64 bytes per indexed key. These are runtime resource policies, not ZIP format limits. Compressed file bytes are
read from a locked stream, not loaded as a whole archive. ZIP comments and Unicode name extensions
are not exported or preserved.

Names cannot be absolute, contain empty/dot/dot-dot components, drive prefixes, NUL, Windows invalid
characters, alternate data streams, reserved device names, or trailing dots/whitespace. Legacy
backslash separators normalize to /. Duplicate names compare ordinally without case, including
file/directory and implicit-parent collisions; unsafe names are rejected rather than rewritten.
Archive links and special files are rejected. Filesystem input/archive paths and output parents
must not traverse reparse points (including symbolic links/junctions).

Create and extract stage their output beside the destination and publish it only after completion,
without replacing an existing destination even if it appears during processing. Normal failures
remove the staging object; cleanup failure is itself Error. Forced host termination may leave a
`.rukbat-zip-<id>.tmp` object. These APIs do not maintain a crash-recovery journal or promise an
atomic snapshot of a changing source tree. A source file is locked against writes/deletion while
it is being read; directory enumeration and other files can change. Source and destination parents
must be under the caller's control; concurrent adversarial filesystem changes are outside the
contract. ACLs, owners, alternate data streams, source attributes and modification times are not
restored by extraction. Select files beforehand with fs operations in a staging source directory.
ZIP is a packaging format, not a complete Windows backup format. Entry traversal when creating
is ordinally sorted; identical ZIP byte output across runtimes is not promised.

```rukbat
import zip
import hash
import encoding

zip.create("./release", "./release.zip")
print(hash.file("./release.zip", hash.sha256))
options := zip.options(encoding.windows_932)
zip.extract("./legacy.zip", "./installed", options)
```

## `process`

The module exports ordinary value structs:

```rukbat
struct Result {
    exit_code: int
    stdout: string
    stderr: string
}

struct EnvironmentVariable {
    name: string
    value: string?
}

struct Input {
    text: string
    encoding: encoding.Encoding
}

struct Options {
    timeout_ms: int?
    output_encoding: encoding.Encoding
    working_dir: string?
    environment: EnvironmentVariable[]
    stdin: Input?
}
```

| Signature | Meaning | Failure |
|---|---|---|
| `process.options(timeout_ms: int?, output_encoding: encoding.Encoding): process.Options` | Creates explicit execution options. working_dir and stdin are null; environment is empty. Null timeout is deliberately unbounded; a deadline is 1 through 2147483647 milliseconds. | Invalid timeout or unsupported encoding causes Error. |
| `process.run(executable: string, arguments: string[], options: process.Options): process.Result` | Starts a contained executable with the supplied execution conditions and captures stdout/stderr. | Invalid conditions or failure to launch, send input, capture, decode, or finish before the deadline causes Error. |

The API accepts required positional arguments only. It does not interpret a shell command string.
The executable and each argument are separate strings; embedded NUL is rejected before launch.
Nonzero exit codes are normal Results. `output_encoding` applies to stdout and stderr and accepts
the same strict encoding values as file text APIs. It controls decoding, not the child's encoding.
Output preserves BOM characters and trailing newlines; empty output is an empty string. Invalid
bytes cause Error. There is no encoding detection or BOM removal. Output streams are drained concurrently.

```rukbat
import encoding
import process

options := process.options(30000, encoding.windows_932)
options.working_dir = "./work"
options.environment = [process.EnvironmentVariable { name: "APP_MODE", value: "batch" }]
options.stdin = process.Input { text: "request\n", encoding: encoding.utf8 }
result := process.run("legacy-tool.exe", ["--report"], options)
print(result.stdout)
```

A nonpositive or oversized timeout is rejected by both options and run (after any field edits),
before launching a child. The process is assigned atomically at creation to a Windows Job Object
with kill-on-close and no breakaway permission. Run waits for the root and descendants, input
transmission and both captures, using one deadline measured from successful creation. Validation,
environment capture and input encoding happen before creation; output decoding follows capture.
This synchronous operation cannot launch a persistent background service. Timeout, input or
capture failure terminates the job. Host termination closes the job and terminates its processes.
Cleanup waits up to five seconds for job termination and five seconds for I/O completion; cleanup
failure raises Error. Null timeout should be reserved for deliberately unbounded operations.

Process containment requires Windows 10 / Server 2016 or later; there is no uncontained fallback.
Only stdin/stdout/stderr handles are inherited. Capture is limited to 64 MiB per stream; exceeding
it terminates the job and raises Error. The job contains ordinary child-process creation, not work
delegated to an existing service or remote machine. Termination does not undo external side effects.

`working_dir: null` uses the captured entry working directory. A specified path resolves against
that directory and must already be an accessible directory. It never changes the parent's location.

`environment` overlays a snapshot of the parent's Windows environment at each run call. Missing
names inherit, a non-null value sets or replaces, an empty string sets an empty value, and null
removes the name from the child. The parent environment and persistent settings are unchanged.
Names compare ordinally without case; duplicate names (including PATH/Path), empty names, names
containing '=' or NUL, values containing NUL and invalid UTF-16 cause Error before launch. Windows
internal inherited drive-state entries are preserved; callers cannot supply such names. Child-only
empty values use a native environment block and do not change env.set's existing empty-value rule.

`stdin: null` supplies immediate EOF without inheriting job-runner input. An Input is encoded
strictly before launch, without BOM or added newline, limited to 64 MiB after encoding, and sent
while stdout/stderr are drained concurrently. The writer closes after transmission to signal EOF.
Empty input also produces immediate EOF. Unencodable text, oversized input and incomplete writes
(including a child closing its input early) cause Error. A completed write proves transmission to
the pipe, not that the child consumed or processed the text. Input encoding is independent of output
encoding. This is bounded text input, not interactive or streaming input.

Executable paths containing a separator or drive prefix resolve against the captured entry directory,
independent of working_dir. Bare names search that entry directory then the effective child PATH;
relative PATH entries also use the entry directory. An extensionless name uses .exe. Arguments are
passed unchanged with Windows C/C++ argv quoting: each Rukbat string is one argv argument. Paths
inside arguments are interpreted by the child, normally against its working_dir. Executables must
be directly launchable binaries; .cmd/.bat are rejected. No shell interpretation is added.


## `http`

HTTP is synchronous, procedural, and independent of PowerShell web cmdlet behavior. It supports
HTTP/HTTPS API requests, text/JSON submission, and bounded file downloads. All arguments are
required and positional. No language syntax, package, or deployment service is added.

`http.Method` is an opaque validated type with read-only values `http.method_get`,
`http.method_head`, `http.method_post`, `http.method_put`, `http.method_patch`,
`http.method_delete`, and `http.method_options`. Methods support same-type equality and ordinary
containers. `http.Body` is opaque immutable response bytes, detached from the connection. It
has no constructor, fields, indexing, equality, or open-stream lifetime. Both types are excluded
recursively from printing, JSON conversion, and PowerShell arguments/results. Use `http.text`
to decode a Body. Ordinary assignment and containers can safely share its immutable storage.

HTTP status constants are read-only `int` values, named `http.status_<number>_<name>`;
for example `http.status_200_ok`, `http.status_404_not_found`, and
`http.status_503_service_unavailable`. `response.status` remains an int and can represent
unassigned/custom codes. There are no aliases omitting the number. Names follow the
[IANA registry](https://www.iana.org/assignments/http-status-codes), including RFC 9110's
`413_content_too_large`, `414_uri_too_long`, `416_range_not_satisfiable`, and
`422_unprocessable_content`. `306_reserved` and the legacy `418_im_a_teapot` explicitly name
reserved codes; `510_not_extended` is obsolete. `104_upload_resumption_supported` is a
temporary registry assignment, not a promise of resumable-upload support.

| Constant | Value |
|---|---|
| `http.status_100_continue` | 100 |
| `http.status_101_switching_protocols` | 101 |
| `http.status_102_processing` | 102 |
| `http.status_103_early_hints` | 103 |
| `http.status_104_upload_resumption_supported` | 104 |
| `http.status_200_ok` | 200 |
| `http.status_201_created` | 201 |
| `http.status_202_accepted` | 202 |
| `http.status_203_non_authoritative_information` | 203 |
| `http.status_204_no_content` | 204 |
| `http.status_205_reset_content` | 205 |
| `http.status_206_partial_content` | 206 |
| `http.status_207_multi_status` | 207 |
| `http.status_208_already_reported` | 208 |
| `http.status_226_im_used` | 226 |
| `http.status_300_multiple_choices` | 300 |
| `http.status_301_moved_permanently` | 301 |
| `http.status_302_found` | 302 |
| `http.status_303_see_other` | 303 |
| `http.status_304_not_modified` | 304 |
| `http.status_305_use_proxy` | 305 |
| `http.status_306_reserved` | 306 |
| `http.status_307_temporary_redirect` | 307 |
| `http.status_308_permanent_redirect` | 308 |
| `http.status_400_bad_request` | 400 |
| `http.status_401_unauthorized` | 401 |
| `http.status_402_payment_required` | 402 |
| `http.status_403_forbidden` | 403 |
| `http.status_404_not_found` | 404 |
| `http.status_405_method_not_allowed` | 405 |
| `http.status_406_not_acceptable` | 406 |
| `http.status_407_proxy_authentication_required` | 407 |
| `http.status_408_request_timeout` | 408 |
| `http.status_409_conflict` | 409 |
| `http.status_410_gone` | 410 |
| `http.status_411_length_required` | 411 |
| `http.status_412_precondition_failed` | 412 |
| `http.status_413_content_too_large` | 413 |
| `http.status_414_uri_too_long` | 414 |
| `http.status_415_unsupported_media_type` | 415 |
| `http.status_416_range_not_satisfiable` | 416 |
| `http.status_417_expectation_failed` | 417 |
| `http.status_418_im_a_teapot` | 418 |
| `http.status_421_misdirected_request` | 421 |
| `http.status_422_unprocessable_content` | 422 |
| `http.status_423_locked` | 423 |
| `http.status_424_failed_dependency` | 424 |
| `http.status_425_too_early` | 425 |
| `http.status_426_upgrade_required` | 426 |
| `http.status_428_precondition_required` | 428 |
| `http.status_429_too_many_requests` | 429 |
| `http.status_431_request_header_fields_too_large` | 431 |
| `http.status_451_unavailable_for_legal_reasons` | 451 |
| `http.status_500_internal_server_error` | 500 |
| `http.status_501_not_implemented` | 501 |
| `http.status_502_bad_gateway` | 502 |
| `http.status_503_service_unavailable` | 503 |
| `http.status_504_gateway_timeout` | 504 |
| `http.status_505_http_version_not_supported` | 505 |
| `http.status_506_variant_also_negotiates` | 506 |
| `http.status_507_insufficient_storage` | 507 |
| `http.status_508_loop_detected` | 508 |
| `http.status_510_not_extended` | 510 |
| `http.status_511_network_authentication_required` | 511 |


The module exports ordinary mutable value structs (names below are qualified by `http`):

```rukbat
struct Header { name: string; value: string }
struct QueryParameter { name: string; value: string }
struct TextBody { text: string; encoding: encoding.Encoding }
struct Request {
    method: http.Method
    url: string
    headers: http.Header[]
    body: http.TextBody?
}
struct Options {
    timeout_ms: int
    max_response_bytes: int
    proxy_url: string?
    use_windows_credentials: bool
    proxy_use_windows_credentials: bool
    max_redirects: int
}
struct Response {
    status: int
    url: string
    headers: http.Header[]
    body: http.Body
}
struct DownloadResult {
    status: int
    url: string
    headers: http.Header[]
    saved: bool
    size: int
    error_body: http.Body?
}
```

| Signature | Meaning | Failure |
|---|---|---|
| `http.request(method: http.Method, url: string): http.Request` | Creates a request with empty headers and null body. | Invalid URL causes Error. |
| `http.options(timeout_ms: int): http.Options` | Creates options with max_response_bytes 67108864, proxy_url null, both credential flags false, and max_redirects zero. | Timeout outside 1 through 2147483647 milliseconds causes Error. |
| `http.send(request: http.Request, options: http.Options): http.Response` | Sends and fully captures a bounded response, including non-2xx responses. | Invalid conditions, connection/TLS failure, timeout, incomplete response, unsupported content coding or limits cause Error. |
| `http.text(body: http.Body, encoding: encoding.Encoding): string` | Strictly decodes captured bytes using the explicit encoding. Preserves BOM and newlines. | Invalid bytes or unsupported encoding causes Error. |
| `http.header_values(headers: http.Header[], name: string): string[]` | Collects all values for a case-insensitive header name; absent names return an empty array. | Invalid header name causes Error. |
| `http.with_query(url: string, parameters: http.QueryParameter[]): string` | Appends UTF-8 percent-escaped query names/values in input order; preserves duplicate names and existing query text. Spaces become %20, not +. | Invalid URL or invalid Unicode causes Error. |
| `http.download(request: http.Request, destination: string, max_bytes: int, options: http.Options): http.DownloadResult` | Streams a GET's status-200 body to a new file and publishes it after completion. Other statuses return saved false with captured error_body and no output file. | Invalid conditions/destination, timeout, transfer failure, limits or publication/cleanup failure cause Error. |

`send` separates an HTTP response from a communication failure. 404, 429, 500 and other final
statuses are ordinary Responses; no implicit success check occurs. Failed text decoding does
not prevent inspecting a previously received status or headers. JSON conversion remains explicit:
`json.parse(http.text(response.body, encoding.utf8))`; known types can use existing `json.parse<T>`.
No Content-Type/charset guessing, BOM stripping or replacement decoding occurs.

URLs must be absolute http/https URLs with a host and no user information, fragment, whitespace,
control characters, backslashes or malformed percent escapes. Request headers use ASCII token
names and ASCII values without controls (horizontal tab is accepted). Host, Content-Length,
Transfer-Encoding, Connection, Expect, Accept-Encoding, Proxy-Authorization, Trailer, TE and
Upgrade are runtime-owned and cannot be supplied. Duplicate Authorization/Content-Type is
rejected. Content-Type requires a body; Content-Type and TextBody.encoding remain separate
explicit choices. All validation, including edited options and strict body encoding, precedes
communication. GET/HEAD reject a body. TextBody encodes without BOM or added newline, up to
64 MiB. Null body and empty text are distinct; empty text creates a zero-length request body.

Options are revalidated on each send/download. max_response_bytes is 1 through 67108864,
and applies to all buffered response bodies, including unsuccessful download responses.
Request and decoded response header collections have a separate 64 KiB budget. Headers retain
separate returned values, including Set-Cookie; original wire ordering/casing/whitespace is not
guaranteed by the underlying HTTP parser. HEAD returns an empty Body regardless of advertised
Content-Length. Protocol upgrades are unsupported. HTTP/1.1 is the common target.

The client requests `Accept-Encoding: identity`. Received application bytes are not transformed;
non-identity Content-Encoding is rejected (except HEAD, where it describes an untransferred body).
Gzip/Deflate/Brotli HTTP content coding is not implemented in this version. Chunked transfer framing
is handled by the HTTP transport and is not part of Body. The byte limit applies while reading,
not only to the declared Content-Length; mismatched/incomplete lengths are errors.

timeout_ms is a shared elapsed deadline from starting transport through response reading,
including followed redirects. Preparation/encoding happens before it. The helper also requests
transport cancellation at the deadline, closes responses, and observes late asynchronous failures.
OS DNS/cancellation and filesystem flush/cleanup can delay returning; this is not a hard real-time
or whole-process deadline. The operations supervisor provides a separate whole-job deadline.
There is no application retry, Cookie jar, cached response, or default Windows credential use.
An Error after sending does not prove the server performed no side effects.

max_redirects zero returns redirects normally. Values 1 through 20 opt GET/HEAD into bounded
301/302/303/307/308 following within the same origin (scheme, host and effective port).
Cross-origin changes, including HTTPS-to-HTTP, return their redirect response. A followed chain
exceeding its bound raises Error. POST and other methods cannot enable redirects; a submitted
body is never replayed by redirect policy. TLS certificate validation stays enabled, OS TLS
defaults are selected per handler, and PowerShell's global TLS settings are not changed.

proxy_url null means direct connection, ignoring ambient proxy/PAC/environment configuration.
A non-null value must be an http proxy origin without credentials, path beyond /, query or
fragment. use_windows_credentials enables the current account's origin challenge authentication;
it cannot be combined with an Authorization header. proxy_use_windows_credentials enables
current-account proxy challenge credentials and requires proxy_url. An explicit Authorization
header can carry a Bearer/Basic value. There is no OAuth flow, explicit password store, automatic
PAC discovery, multipart upload, streaming upload, WebSocket or resumable download API.
Internal clients reuse connections by proxy/credential configuration with a bounded cache;
no client/session object or dispose operation is exposed to Rukbat.

Downloads require GET without a body and a positive signed-64-bit max_bytes. Only status 200
publishes a complete file; 204, 206, 3xx and non-2xx return saved false, size zero and a non-null
error_body (which may be empty). A saved result has its actual byte size and null error_body.
The successful file uses max_bytes rather than the memory response limit. A destination must
not exist, its parent must exist, and its ancestry must contain no reparse points. Relative
paths use the captured entry working directory. Content-Disposition never chooses the path.
Output is staged beside the destination, flushed, and published by a non-replacing move.
Ordinary failures delete the stage; a cleanup failure is Error. A concurrent destination creator
is preserved. Forced host termination can leave `.rukbat-http-<id>.tmp`; there is no crash journal
or filesystem durability/hostile-parent guarantee. No existing destination is replaced.

HTTP uses the target's installed System.Net.Http facilities with a C# 5 helper. Desktop hosts
require an installed .NET Framework handler supporting OS TLS defaults (4.7.1 or later).
No dependency is bundled into deployment scripts. As with other native helpers, FullLanguage
and permission to use Add-Type are required. Actual proxy/Windows-auth access depends on the
deployment account and network configuration.

```rukbat
import http
import encoding
import json

options := http.options(30000)
request := http.request(http.method_get, "https://example.com/api/jobs")
response := http.send(request, options)
if response.status == http.status_200_ok {
    document := json.parse(http.text(response.body, encoding.utf8))
    print(json.kind(document))
}
```

## Runtime path resolution

Direct `powershell -File` / `pwsh -File` execution, including `rukbat run`, captures the host process's current directory before initializing modules. A PowerShell statement invoking the script instead captures that session's filesystem location. These entry directories treat characters such as `[` and `]` literally; the script directory remains separate and is available through `path.script_dir()`.

Standard filesystem operations resolve nonempty relative paths against the captured entry working directory before calling PowerShell or .NET. Absolute paths stay absolute. A root-relative path such as `\data\file.txt` uses the captured directory's drive root. Drive-relative forms such as `C:file.txt` are rejected because they depend on implicit per-drive state; `fs.is_file` and `fs.is_dir` return `false` for them. Invalid/empty operation paths raise Error, except for these existence predicates. This does not change the Error contracts of read, write, list, copy, move, create, or remove.

## Program arguments

`args(): string[]` returns command-line arguments supplied to the entry program in order. It returns `[]` when none were supplied. Use `cli` for declarative argument definitions, validation and help; the program decides when to display help, exit, and perform operations.

## `cli`

Command definitions are ordinary mutable value structs. Factory functions populate all fields;
configure a definition before placing it in a command array, because ordinary value-copy rules apply.
The module exports the following structs (all type names below are qualified by `cli`):

```rukbat
struct Command {
    name: string
    description: string
    arguments: cli.Argument[]
    options: cli.Option[]
    flags: cli.Flag[]
}
struct Argument {
    name: string
    description: string
    required: bool
    repeatable: bool
    allow_empty: bool
    choices: string[]
}
struct Option {
    name: string
    description: string
    short_name: string?
    value_name: string
    required: bool
    default_value: string?
    repeatable: bool
    allow_empty: bool
    choices: string[]
}
struct Flag {
    name: string
    description: string
    short_name: string?
}
```

`cli.Result` is an opaque immutable value with no public fields or constructor.
It supports ordinary assignment, arguments, returns and containers; it cannot be printed,
compared for equality, converted to/from JSON, or passed through PowerShell boundaries,
including inside arrays/structs. Read strings and arrays explicitly before exporting data.

| Signature | Meaning | Failure |
|---|---|---|
| `cli.command(name: string, description: string): cli.Command` | Creates a command with empty argument, option and flag arrays. name is the displayed invocation label, not inferred from the host process. | Definition validation occurs in parse/help. |
| `cli.argument(name: string, description: string): cli.Argument` | Creates a required, non-repeatable positional argument. allow_empty is false and choices is empty. | Definition validation occurs in parse/help. |
| `cli.option(name: string, description: string): cli.Option` | Creates an optional value option. short_name/default_value are null, value_name is the invariant uppercase name, required/repeatable/allow_empty are false and choices is empty. | Definition validation occurs in parse/help. |
| `cli.flag(name: string, description: string): cli.Flag` | Creates a presence flag with no short name. | Definition validation occurs in parse/help. |
| `cli.parse(command: cli.Command, arguments: string[]): cli.Result` | Validates definitions and parses literal arguments without writing streams, exiting, reading files or executing commands. The result retains independent effective values. | Invalid definition or input causes Error. Help requests suppress input errors, not definition errors. |
| `cli.help(command: cli.Command): string` | Validates definitions and generates deterministic help in definition order, with LF separators and no trailing newline. | Invalid definition causes Error. |
| `cli.help_requested(result: cli.Result): bool` | Reports a recognized --help or -h before the option terminator. | None. |
| `cli.has(result: cli.Result, name: string): bool` | Reports a supplied flag or an effective value, including a default or explicitly allowed empty value. | Unknown name or help-mode result causes Error. |
| `cli.get(result: cli.Result, name: string): string` | Reads one non-repeatable argument/option value. | Unknown name, help mode, flag, repeatable item, or absent value without a default causes Error. |
| `cli.get_all(result: cli.Result, name: string): string[]` | Reads repeatable values in supplied order; an absent optional item returns []. The returned array is independent. | Unknown name, help mode, flag or non-repeatable item causes Error. |

Definition names must match `[a-z][a-z0-9]*(-[a-z0-9]+)*`, without a leading `--`.
Names are unique across arguments, options and flags. `help` is reserved.
Short names are null or one lowercase ASCII letter, unique across options/flags; `h` is reserved.
Command name must be nonblank and contain no control characters. Option value_name must be
nonblank and contain no whitespace/control characters. Descriptions may contain Unicode/newlines.

Required positionals precede optional ones. A repeatable positional must be the last argument;
required means at least one supplied value. Options may also be required or repeatable.
A non-null default is permitted only on an optional, non-repeatable option. It must satisfy
allow_empty and choices. Choice strings are unique and compared exactly; an empty choices array
accepts any value. An empty choice requires allow_empty. Empty strings are not trimmed or
replaced by defaults. Required flags, negative flags and boolean-valued flag syntax are not provided.

Long value options accept `--name VALUE` and `--name=VALUE`; short names accept `-n VALUE`
and `-n=VALUE`. Only the first equals sign splits a token. Flags take no value. Options may be
mixed with positionals; positional order is retained. Duplicate flags/non-repeatable options
cause Error even when the values match. Short clusters (`-abc`), joined values (`-ofile`),
abbreviated/case-insensitive names and implicit negation are not accepted.

A bare `--` ends option recognition and is not a value. Later tokens are all positionals.
A bare `-` is a value. Negative numeric spellings matching
`-[0-9]+(\.[0-9]+)?([eE][+-]?[0-9]+)?` are values, without conversion or range checking.
Other dash-prefixed values require `--name=VALUE` (including `--name=--`) or the positional
terminator. A pending option does not consume the next dash-prefixed nonnumeric token;
it reports a missing value. No quoting, path resolution, glob expansion, environment/config
merging, response files or subcommand processing is performed. Numeric/domain validation belongs
to the caller, using existing text conversion operations where appropriate.

Exact `--help` and `-h` before `--` request help even after unknown/duplicate/missing inputs;
`--name=--help` is a value and `-- --help` is positional data. A help result can only be queried
with help_requested; other getters reject it. The caller explicitly prints help and exits before
operations. Imported top-level initialization still occurs before the caller's help branch.

Input Errors report the first affected argument in supplied order, followed by brief usage and
`Use --help for details.` Missing required items follow token errors, in definition order
(arguments, then options). An unknown spelling may suggest exactly one declared spelling one
insertion/deletion/substitution away; ambiguous suggestions are omitted and never applied.
Definition Errors are distinguished by the `Invalid CLI definition:` prefix.
Generated help lists arguments, then value options, flags and the built-in help flag, preserving
each array's order. Descriptions are indented on following lines. Required/repeatable markers,
defaults and choices are included; formatting does not depend on terminal width or locale.

See [Command-line programs](CLI.md) and the [file-browser example](../examples/file-browser/options.rukbat).
The runtime is emitted as PowerShell 5.1-compatible helpers; importing cli alone needs no Add-Type
or filesystem/process/encoding helper. Adding the canonical standard module name `cli` reserves
it as a relative-import alias, following the existing standard-module rule.

## `test`

| Signature | Meaning | Failure |
|---|---|---|
| `test.assert(condition: bool): void` | Verifies a condition evaluated once. | False raises Error containing the source expression and source location. Direct primitive comparisons also show both evaluated values. |
| `test.fail(message: string): void` | Explicitly fails with a reason. | Always raises ordinary Error with that message. |

The module exports `test.Context`, an ordinary struct with `temp_dir: string` and
`source_dir: string`. `rukbat test` passes it to tests declaring a single parameter
of that exact type. `temp_dir` is the test's unique temporary working directory;
`source_dir` is the directory containing its source file. Both are absolute paths.

Assertions use ordinary Rukbat type checking, evaluation order, numeric conversions,
case-sensitive comparisons, and short-circuit boolean semantics. They accept only
positional arguments and no type argument. Errors are catchable, including assertion
errors; an assertion swallowed by a `catch` does not fail the test runner. No automatic
deep equality is added to the language. Compare array lengths and elements explicitly.
The module also works in regular standalone `build`/`run` output and needs no testing
runtime on deployment machines.

See [Testing Rukbat programs](TESTING.md) for discovery, execution, temporary fixtures,
multiple cases, expected errors, and command-line exit codes.
