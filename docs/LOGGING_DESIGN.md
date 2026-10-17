# Logging design

The [standard library](STDLIB.md#log) defines the API, configuration, record format,
and failure behavior.

## Configuration and delivery

An ordinary `log.Logger` value keeps configuration explicit and follows Rukbat's
copy semantics. Functions receive their logger as an argument. Severity-specific
procedures need no overloads, callbacks, or new language syntax. `log.enabled`
lets callers guard expensive message construction with ordinary control flow.

The default writes INFO and higher to stderr. File output is opt-in, with a
separate threshold. Both destinations receive the same prepared record. Each
eligible destination is attempted even if the other fails; an aggregated Error
reports delivery failures without discarding successful output.

Each eligible file write opens, appends, flushes, and closes its file. This costs
a synchronous flush per record but requires no lifecycle or shutdown API. Append follows
`fs.append_text` sharing rules; it is not a concurrent-writer protocol.

## Compiler and runtime

| Component | Responsibility |
|---|---|
| `StandardLibrary/LogValues.cs` | Level identity, constants, labels, and operation metadata. |
| `StandardLibrary/StandardModules.cs`, `StandardSignatures.cs` | Module, Logger struct, and fixed signatures. |
| `Semantics/` | Opaque Level rules and recursive external-boundary checks. |
| `CodeGeneration/LogEmission.cs` | Logger field adaptation and factory defaults. |
| `Runtime/PowerShell/LogRuntime.ps1` | Shared threshold selection, path resolution, and destination error aggregation. |
| `Runtime/Native/Log.cs.txt` | C# 5 record formatting, strict encoding, and synchronous delivery. |

The native helper is embedded in the generated script and loaded only for an
eligible write. Creation, configuration, enabled checks, and filtered writes need
no Add-Type call. Threshold selection stays in the PowerShell helper rather than
being duplicated across writing functions or native code.

Native calls carry configuration, message, and prepared records in arrays.
Passing a large string directly to PowerShell's native method binder causes
invocation-copy overhead. The array boundary applies to formatting and delivery,
including stderr writes. The runtime retains no messages, records, or open streams
between calls; memory use depends on the current record rather than log history.
