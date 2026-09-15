# Rukbat legacy prototype library

This directory contains reference material from before standard modules were introduced. New v0.1 code should use `import files` and `import json`. The official APIs and behavior are defined in [`docs/STDLIB.md`](../docs/STDLIB.md).

The following describes the old prototype: a small library exposing existing PowerShell commands through typed Rukbat functions.
It provides file listing, text saving, and JSON encoding of string arrays.

Historical usage from a program in the repository root:

```text
import "./lib/files.rukbat" as files
import "./lib/json.rukbat" as json

names: string[] = files.list_files(".", "*.md")
files.write_text("files.json", json.encode_strings(names))
```

The prototype allowed copying the lib directory into a project. Import paths are relative to the importing source file. The library's `.rukbat` files are required at compile time; only the generated `.ps1` is needed at runtime. There is no global standard-library search or package management.

| Module | Function | Behavior |
|---|---|---|
| `files.rukbat` | `list_files(directory: string, pattern: string): string[]` | Filters direct-child filenames using Get-ChildItem's Filter parameter. |
| `files.rukbat` | `write_text(path: string, text: string): void` | Writes UTF-8 without a trailing newline, overwriting existing files. |
| `json.rukbat` | `encode_strings(values: string[]): string` | Uses ConvertTo-Json to encode a string array as a compact JSON array. |

File operations use LiteralPath, so brackets in directory or output names are not interpreted as wildcards. Relative paths use the runtime working directory.

`list_files` does not recurse or include directories. It normally excludes hidden files and does not guarantee enumeration order. Typical patterns are `*` and `*.txt`; matching follows PowerShell's file-system filter.

`write_text` does not create parent directories. Access and save failures propagate as Error; callers can use try/catch when needed. Overwrites are not atomic and do not guarantee preservation of existing contents if saving fails partway through. Windows PowerShell 5.1 writes a UTF-8 BOM; PowerShell 7 does not.

`encode_strings` returns `[]` for an empty array and `["value"]` for a single element. ConvertTo-Json handles escaping quotes and control characters. The prototype supports string arrays, not arbitrary structs.

The `examples/file-browser` example combines argument handling, filtering, JSON output, and error exit codes. It now uses the standard modules rather than these prototype wrappers.
