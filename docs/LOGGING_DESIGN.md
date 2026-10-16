# Logging library design

Status: implemented. This document explains the `log` module's design and
verification requirements. [STDLIB.md](STDLIB.md#log) defines its public APIs;
[LANGUAGE.md](language.md) defines ordinary value and evaluation semantics.

## Purpose and normal use

Provide readable diagnostic logging for Windows scripts with little setup.
Use ordinary procedures and mutable configuration values. Generated scripts
remain standalone and compatible with Windows PowerShell 5.1.

```rukbat
import log

logger := log.create()
log.info(logger, "Starting")
log.warn(logger, "Skipped an unavailable file")
```

The default is INFO and higher to stderr. To retain the same records in a file,
add one assignment:

```rukbat
logger.file = "job.log"
log.info(logger, "Starting")
```

Configuration uses ordinary field assignment. For DEBUG records in the file
and INFO and higher on stderr:

```rukbat
logger.file = "job.log"
logger.file_level = log.level_debug
```

For file-only logging, set `logger.stderr_level = null`. Set both
`logger.stderr_level` and `logger.file` to null to disable all output.
The caller creates parent directories explicitly when necessary.

## Public API

| Signature | Behavior |
|---|---|
| `log.create(): log.Logger` | Returns the default configuration. Performs no I/O. |
| `log.debug(logger: log.Logger, message: string): void` | Writes a DEBUG record to eligible destinations. |
| `log.info(logger: log.Logger, message: string): void` | Writes an INFO record to eligible destinations. |
| `log.warn(logger: log.Logger, message: string): void` | Writes a WARN record to eligible destinations. |
| `log.error(logger: log.Logger, message: string): void` | Writes an ERROR record to eligible destinations. |
| `log.enabled(logger: log.Logger, level: log.Level): bool` | Reports whether at least one destination's threshold accepts the level. |

These are fixed positional signatures. There are no overloads, omitted
arguments, generic write function, or level-specific aliases. The four writing
functions are the canonical operations for their respective severities.
Messages must be strings; format values explicitly with existing APIs.

### Configuration

`log.Logger` is an ordinary public struct:

```rukbat
struct Logger {
    stderr_level: log.Level?
    file: string?
    file_level: log.Level
}
```

| Field | Default | Meaning |
|---|---|---|
| `stderr_level` | `log.level_info` | Minimum stderr severity; null disables stderr. |
| `file` | `null` | File path; null disables file output. |
| `file_level` | `log.level_info` | Minimum file severity; ignored while file is null. |

The struct owns no stream, buffer, registry entry, or other external resource.
It can be constructed with a struct literal containing all three fields.
`create()` supplies convenient defaults, not privileged constructor invariants.
There is no separate Options, Output, Handler, or formatter type.

All ordinary value-copy rules apply:

```rukbat
verbose := logger
verbose.stderr_level = log.level_debug
// logger retains its previous settings.
```

Parameters, return values, arrays, dictionaries, nullable values, and struct
fields preserve those rules. A function cannot reconfigure its caller's Logger
by mutating a copied parameter. No global/default Logger is installed; imported
libraries receive a Logger explicitly when they need to log.

### Levels

`log.Level` is an opaque validated value with four read-only constants:

| Constant | Record label | Increasing severity |
|---|---|---|
| `log.level_debug` | `DEBUG` | Lowest |
| `log.level_info` | `INFO` | |
| `log.level_warn` | `WARN` | |
| `log.level_error` | `ERROR` | Highest |

A minimum accepts that level and all higher levels. There are no custom numeric
levels, TRACE, FATAL, OFF, aliases, or implicit string/numeric conversions.
Disabling uses the destination configuration described above.

Non-null Level values support ordinary storage and same-type equality. Nullable
Level values follow the existing null-comparison and narrowing rules; equality
between two nullable values is not added. Level has no ordering operators,
arithmetic, public fields, or struct literal. Its internal ranking is not a public
numeric representation.

Level is recursively excluded from direct printing, JSON, and PowerShell
arguments/results. Logger inherits the JSON/PowerShell restriction through its
Level fields; it is still an ordinary inspectable, copyable struct.

## Filtering and evaluation

Rukbat evaluates arguments once, left to right, before calling a function.
`log.info(logger, make_message())` first copies the Logger value, then evaluates
the message. If `make_message()` changes a module-level Logger, that change
does not alter the snapshot already passed to this call.

Filtering never suppresses argument evaluation or its errors. For expensive
message construction, use an explicit guard:

```rukbat
if log.enabled(logger, log.level_debug) {
    log.debug(logger, "Loaded " + text.from_int(count) + " records")
}
```

This excerpt assumes `text` is imported and `count` is an existing integer.
It uses normal control flow rather than lazy callbacks or special syntax.

Stderr is eligible when `stderr_level != null` and accepts the record severity.
File output is eligible when `file != null` and `file_level` accepts it.
`enabled` returns the OR of those predicates. It does not validate a file path,
inspect the filesystem, obtain a timestamp, load a native helper, or guarantee
that a subsequent write will succeed. An empty or unwritable configured file
can therefore make `enabled` return true.

If no destination is eligible, a writing function returns without message
validation, clock lookup, path resolution, formatting, encoding, native-helper
loading, or I/O. A configured but filtered file cannot interfere with stderr.

## Record format

Use one fixed, human-readable text format:

```text
time=2026-10-16T09:00:00.1234567Z level=INFO message="Starting"
```

The actual record ends with exactly one CRLF. Field order, lowercase field names,
single spaces between fields, and uppercase severity labels are fixed.

The timestamp is captured once per enabled call and shared by both destinations.
It uses the same canonical UTC spelling as `datetime.stringify`:
`yyyy-MM-ddTHH:mm:ss.fffffffZ`. Seven fractional digits describe representation,
not clock accuracy. Timestamps use the wall clock; they are neither sorted nor
guaranteed unique or monotonic.

The message is always a quoted JSON string, embedded in the text record:

- Escape quote and backslash as `\"` and `\\`.
- Use `\b`, `\t`, `\n`, `\f`, and `\r` for those control characters.
- Encode remaining U+0000 through U+001F, U+007F through U+009F, U+2028,
  and U+2029 as lowercase `\uXXXX` escapes.
- Preserve other valid Unicode scalars, including supplementary characters.
- Reject unpaired UTF-16 surrogates before attempting any output.

Empty messages are valid and appear as `message=""`. Embedded line terminators
and terminal escape characters do not create additional physical records or
ANSI control sequences. This is a text format, not a JSON object/JSON Lines API,
and is not a general sanitizer or secret-redaction mechanism.

Stderr receives the complete record through the existing `Console.Error` writer,
then that writer is flushed. The library does not replace the writer, change
console encoding, or reinterpret PowerShell redirection. Generated scripts retain
their existing UTF-8 console setup. File bytes are always strict UTF-8 without
a BOM, including newly created files. No color or culture-dependent formatting
is added.

## File output

Resolve an eligible file path using the existing captured entry working directory,
as other filesystem APIs do. Later PowerShell or .NET working-directory changes
do not change that base. Assigning a new path to Logger explicitly changes the
configured destination. Use `path.script_dir()` explicitly for script-relative logs.

File output follows the append contract already used by `fs.append_text`:

1. Prepare strict UTF-8 bytes before opening the file.
2. Open with `FileMode.Append`, `FileAccess.Write`, and `FileShare.Read`.
3. Write the prepared record, call `Flush(true)`, and close before returning.

A missing file is created on the first eligible write. Parents must already exist.
Existing bytes are neither decoded, rewritten, truncated, nor scanned. An existing
file must be a compatible UTF-8 log ending at a record boundary. There is no BOM
insertion, encoding detection, tail repair, or implicit newline repair.

Overlapping writers can fail with a sharing Error. There is no cross-process
atomic-append guarantee, waiting, lock retry, or record-order guarantee across
processes. Reader sharing must be compatible in both directions; in particular,
an active `fs.lines` traversal is not a supported concurrent log tailer.

I/O failure can leave a partial record. A later append does not repair that tail.
Flush and close completion do not establish durability across physical power loss
or storage devices that do not honor flush requests. Each record's synchronous
flush is an intentional cost; no buffered or background mode is included.

## Failure behavior and two destinations

Each writing call has two phases:

1. **Record preparation:** select destinations, validate the message, obtain one
   timestamp, format one record, and prepare its bytes if file output is selected.
   A shared record-preparation error produces no output.
2. **Delivery:** attempt eligible stderr first, then eligible file output. File
   path resolution belongs to the file attempt, using the existing path resolver.
   An invalid file path is a file-output failure and does not suppress eligible
   stderr output. An ordinary stderr write/flush failure does not prevent the
   file attempt. After both attempts, throw one ordinary Rukbat Error if either
   destination failed. Do not use existence/permission preflight checks as
   delivery guarantees.

The Error identifies each failed destination and includes its failure description.
For a file it includes the resolved path, or the supplied path when resolution
failed. Preserve a primary write/flush error if close also fails, and include
the cleanup failure under the same destination.
Do not include the potentially large or sensitive message in delivery-error text.
Normal Rukbat error handling associates the failure with the logging call location.

Successful output to one destination is not rolled back after another fails.
An Error does not imply that nothing was written, and retrying can duplicate a
record. The library neither retries nor chooses an unconfigured fallback.

`log.error` severity does not throw, exit, or alter the exit code merely because
it is ERROR. Only an actual preparation/delivery failure raises Error. Logging
inside a catch can itself fail and replace the original error under normal
language semantics; callers that must preserve the original error handle the
logging error separately before rethrowing it.

Logging emits no success-stream/stdout value. It does not use PowerShell
Write-Error/Write-Warning/Write-Verbose, change host preference variables, or
install process-exit hooks. Ordinary runtime failures remain ordinary errors;
this contract does not promise cleanup after forced process termination.

## Implementation boundaries

No parser syntax, optional arguments, overloads, ownership rules, or user-defined
enum facility are required. Do not add a production package dependency.

| Area | Required integration |
|---|---|
| `StandardLibrary/LogValues.cs` | Own Level identity, constants, labels, severity metadata, and fixed operation metadata. |
| `StandardLibrary/StandardModules.cs` | Register `log` and the ordinary Logger struct. No dependency on the whole fs, datetime, or JSON module. |
| `StandardLibrary/StandardSignatures.cs` | Add the six fixed signatures using this compilation's canonical Logger type. |
| `Semantics/` | Resolve Level, constants and equality; permit Logger literals; extend recursive external-boundary checks. |
| `CodeGeneration/LogEmission.cs` | Adapt bound Logger field keys once, preserving normal argument copying and evaluation order; construct factory defaults. |
| `Runtime/PowerShell/LogRuntime.ps1` | Apply shared thresholds, orchestrate destination attempts and path resolution, and aggregate destination-labelled failures; return before native loading when filtered. |
| `Runtime/Native/Log.cs.txt` | C# 5-compatible record formatting, strict encoding, synchronous stderr/file I/O, and primary/cleanup failure preservation. |
| Documentation/editor tooling | STDLIB/LANGUAGE contracts, completion/hover/signature help, and readable Level/Logger debugger inspection. |

Keep severity metadata centralized; do not independently implement threshold
rules in each writing function or duplicate them in PowerShell and native code.
Ordinary Logger copying needs no new runtime value-storage type.

The native entry receives an argument array containing the prepared configuration
and message, rather than exposing a large string argument to PowerShell's native
method binder. Stderr delivery also occurs inside native code; a direct generated
`[Console]::Error.Write(large_string)` would reintroduce invocation-copy overhead.
Apply the array/prepared-bundle boundary to formatting and every delivery call;
do not unpack a large record into a native string argument between those steps.
Do not retain Logger instances, messages, records, or streams in a global registry.

Native code is embedded in the standalone script and loaded only for an eligible
write. Import, factory creation, configuration edits, enabled checks, and filtered
writes do not execute Add-Type. Existing FullLanguage/Add-Type restrictions still
apply when an eligible write needs the helper.

Adding a canonical standard module name reserves `log` as an import alias under
the existing module rule. This compatibility impact must be documented and tested.

## Verification requirements

- Check all six signatures, wrong arguments/results, four constants, Level equality,
  nullable thresholds, Logger literals, and invalid Level construction/order/arithmetic.
- Exercise ordinary copies, consts, fields, arrays, dictionaries, parameters, returns,
  module calls, and Logger snapshot preservation when message evaluation mutates a
  module-level Logger. Check recursive JSON/PowerShell/printing exclusions.
- Test every record level against every destination threshold, disabled stderr,
  absent file, fully disabled Logger, and independently filtered file configuration.
  Prove filtered calls skip native loading, validation and filesystem work, while
  argument side effects and errors still occur once in normal order.
- Verify default stderr operation, adding a file with one assignment, file-only
  operation, and different stderr/file thresholds in actual generated scripts.
- Pin exact record formatting, empty messages, Unicode, escaping, malformed UTF-16,
  CRLF and no BOM, culture independence, and identical records at both destinations.
- Exercise relative/Japanese/metacharacter paths, cwd changes, missing parents,
  append preservation, sharing conflicts, file release after every call, and reuse
  of a compatible existing UTF-8 log. State partial-tail limits explicitly.
- Fault-test stderr failure with a successful file attempt, file failure after
  successful stderr output (including an invalid selected path), both failures,
  and primary-error preservation during cleanup. Invalid message preparation
  must produce no output. Test that ERROR severity alone changes neither control
  flow nor exit code.
- Run normal/debug output on PowerShell 5.1 and 7, source-free standalone scripts,
  CLI/editor integration, formatting, diagnostics, completion, and debugger display.
- Measure many fixed-size records while the file grows, then huge messages as a
  separate condition. Retained storage must not grow with past record count/file
  size. Filtered calls must allocate independently of message length after argument
  construction; enabled calls may allocate O(current record size). Verify the real
  generated PowerShell 7 boundary, not only direct C# formatter calls.
- Measure synchronous flush throughput separately from correctness; do not add
  fixed elapsed-time assertions or claim performance without measurements.

Focused regression coverage lives in `LogTypeTests`, `LogTests`, `LogNativeTests`,
`LogAllocationTests`, and the language-server/editor/debugger tests. Memory and
throughput measurements must identify the host, input dimensions, warm-up and
measurement method; a passing functional test does not establish performance.

## Scope

The initial module provides the common call path and two built-in destinations.
There is no global configuration, hierarchy, custom handler, format template,
structured-field API, JSON output mode, automatic exception capture, source-location
inference, rotation/retention, async queue, remote service, Event Log integration,
or concurrent-writer protocol. These are independent features, not prerequisites
for implementing the six operations above.

## Research and rationale

| Primary reference | Relevant observation and Rukbat decision |
|---|---|
| [Go log/slog](https://pkg.go.dev/log/slog) | Severity thresholds and enabled checks are useful. Go also documents eager argument evaluation. Use explicit guards in Rukbat; do not add callback/lazy-value machinery. |
| [Go MultiHandler source](https://go.dev/src/log/slog/multi_handler.go) | Enabled destinations are attempted and failures collected. Use that delivery principle for the two built-in destinations, without a public handler framework. Rukbat surfaces failures as Error rather than discarding them. |
| [Python logging](https://docs.python.org/3/library/logging.html) | Familiar severity-specific calls reduce ceremony. Logger hierarchy/propagation can duplicate delivery; Rukbat keeps explicit, ordinary configuration values. |
| [Rust log](https://docs.rs/log/latest/log/) | Macro logging may suppress argument evaluation. Ordinary Rukbat functions deliberately retain the language's eager evaluation rules. |
| [Go log](https://pkg.go.dev/log) | Its Fatal helpers exit after logging. Rukbat keeps logging and process termination separate. |
| [.NET FileMode](https://learn.microsoft.com/en-us/dotnet/api/system.io.filemode?view=netframework-4.8.1), [Framework FileStream source](https://github.com/microsoft/referencesource/blob/main/mscorlib/system/io/filestream.cs), [Windows sharing rules](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew) | Append and sharing modes do not establish an atomic multi-process logging protocol. Preserve the narrower existing append contract. |
| [.NET UTF8Encoding](https://learn.microsoft.com/en-us/dotnet/api/system.text.utf8encoding.-ctor?view=netframework-4.8.1), [FileStream.Flush](https://learn.microsoft.com/en-us/dotnet/api/system.io.filestream.flush?view=netframework-4.8.1) | Use strict BOM-free encoding and synchronous flush; do not claim universal physical durability. |

Repository constraints come from [PHILOSOPHY.md](PHILOSOPHY.md),
[LANGUAGE.md](language.md), [STDLIB.md](STDLIB.md), and
[ARCHITECTURE.md](ARCHITECTURE.md). In particular, ordinary configuration follows
existing process/HTTP options; file behavior follows fs.append_text; stderr follows
eprint; and implementation must preserve the text runtime's argument-array boundary.
