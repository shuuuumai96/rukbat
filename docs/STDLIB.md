# Auvil Standard Library

## Confirmed specification

The standard library defines facilities available to an Auvil program. Its public contract is independent of compiler layout and PowerShell implementation details.

Standard modules use their canonical names:

```auv
import files
import json
```

## `files`

| Signature | Meaning | Failure |
|---|---|---|
| `files.list(path: string, pattern: string): string[]` | Returns the names of files directly contained by `path` that match `pattern`. It does not recurse and does not guarantee order. | A path that cannot be listed causes a runtime error. |
| `files.write(path: string, text: string): void` | Replaces the contents of `path` with text. Parent-directory behavior and encoding are not yet specified. | Failure behavior is not yet specified. |

The text encoding and BOM policy are not yet specified.

## `json`

| Signature | Meaning | Failure |
|---|---|---|
| `json.encode(value: T): string` | Encodes a supported Auvil value as JSON. Arrays and structs preserve their JSON shape. | A value that cannot be encoded causes a runtime error. |
| `json.decode<T>(text: string): T` | Decodes JSON text as the explicitly requested Auvil type. | Invalid JSON or a value incompatible with `T` causes a runtime error. |

The library has one JSON encoding operation and one JSON decoding operation. It does not expose type-specific names such as `encode_strings`.

## Program arguments

`args(): string[]` returns the command-line arguments supplied to the entry program, in order. It returns an empty array when no arguments were supplied. Argument parsing is the responsibility of the program.

## Not yet specified / TBD

The text encoding and BOM policy; supported JSON types; JSON type mapping; null; missing and extra fields; numeric conversion; and the error model are not yet specified.

## Current implementation behavior

The repository currently contains experimental relative modules with narrower APIs. Their names, placement, import mechanism, PowerShell implementation, and behavior are not standard-library specification.

## Failures

Until Auvil's full error model is specified, an operation that cannot complete reports a runtime error. This document does not define exception types, result types, recovery, or implementation mechanisms.
