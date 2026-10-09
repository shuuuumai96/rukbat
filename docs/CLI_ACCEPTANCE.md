# Practical CLI acceptance scenario: batch file validation

The implemented [batch-check example](../examples/batch-check/main.rukbat) reads
JSON configuration, finds files recursively, invokes a user-supplied checker, and
writes a JSON report. This document defines its acceptance contract. Compiler
exit codes are defined in [the language specification](language.md#commands-and-arguments).

## Invocation and configuration

Invocation:

```powershell
rukbat run examples/batch-check/main.rukbat -- config.json report.json
```

Exactly two nonempty positional arguments are required. A sole `--help` prints usage to stdout and exits with code `0` without reading files or launching a checker.
All relative paths, including paths in the configuration, resolve against the process working directory, not the configuration file's directory.
The report must be outside the input directory tree and its parent directory must already exist. The first version documents this as an invocation precondition; it does not add a path-containment API.

Configuration schema:

```rukbat
struct Config {
    directory: string
    pattern: string
    executable: string
    arguments: string[]
}
```

Example configuration (the checker executable is illustrative):

```json
{"directory":"input","pattern":"*.txt","executable":"C:/tools/checker.exe","arguments":["--strict"]}
```

`directory`, `pattern`, and `executable` must be nonempty. Parse with `json.parse<Config>` and its existing strict type rules.
For each matching file, copy the configured arguments, append the file's absolute path as one argument, and call `process.run` with a fixed 30-second timeout and explicit `encoding.utf8` output encoding. The checker must produce UTF-8 output.
Do not build a shell command string or interpolate the path into code. Run checkers sequentially.
A checker exit code of zero means that file passed. Any nonzero checker code means it failed, but does not stop checks of the remaining files. A timeout terminates the contained checker process tree and stops the run with Error. Work delegated to an existing service or remote host is outside that containment; see [process](STDLIB.md#process).

## Report and streams

The report is a JSON array of these records:

```rukbat
struct CheckResult {
    file: string
    exit_code: int
    stdout: string
    stderr: string
}
```

Each `file` is an absolute path. Preserve each checker's exit code and captured stdout/stderr exactly, including trailing newlines.
Report order follows enumeration order and is not guaranteed. Tests compare records by path rather than relying on order.
No matches produce `[]` without launching a checker. One match still produces an array.
Save only after all checks finish, using `fs.write_text(..., encoding.utf8)` and `json.stringify`: UTF-8 without a BOM or trailing newline.
An existing report is replaced on completed runs, including runs with failed checks. `fs.write_text` stages complete encoded report contents in the report directory before the final replacement; a conversion failure or failure before that step leaves an existing report unchanged. The final filesystem replacement has no durability or universal atomicity guarantee.

On a normal run, stdout is empty; checker streams belong only in the report. Operational errors produce an English diagnostic through `eprint`.

| Program exit code | Meaning | Report behavior |
|---|---|---|
| `0` | All checks passed, no files matched, or help was requested. | Saved for a normal run; untouched for help. |
| `1` | At least one checker returned nonzero. | Saved with all completed results. |
| `2` | Invalid arguments/configuration, enumeration failure, process API Error, or report-write failure. | No write before the final save; a pre-replacement save failure leaves an existing report unchanged. |

Enumeration completes before starting any checker. A process API Error stops the run rather than inventing a checker exit code or writing a partial report.
Checker side effects cannot be rolled back. This example is intended for read-only validators; executable selection remains the caller's responsibility.

## Acceptance tests for the completed example

- Check nested files and direct children while excluding nonmatches and directories. Confirm each file is passed once as a separate final argument.
- Cover no matches and one match; assert JSON array shape and BOM-free output without a trailing newline.
- Cover spaces, brackets, non-ASCII filenames, and shell metacharacters in paths and arguments. Verify that configured arguments do not accumulate file paths across iterations.
- Use a deterministic test checker to produce successful and unsuccessful results, multiline UTF-8 stdout/stderr, and empty streams. Verify captured values and the program exit code; mixed results must include all files.
- Reject missing/extra/empty arguments and malformed or incorrectly typed configuration. Verify stderr and code `2`, with an existing report unchanged.
- Cover missing roots, file-as-root, inaccessible traversal where test permissions allow it, invalid patterns, and a missing executable. Do not silently skip unavailable permission fixtures: report their limitations.
- Cover hidden files/subtrees and reparse-point handling without following a directory cycle. Tests requiring link-creation privileges must report unavailable fixtures explicitly.
- Verify failed report saves return failure and pre-replacement failures preserve an existing report, as specified above.
- Verify relative paths from a working directory different from the configuration directory.
- Compile and run the example on Windows PowerShell 5.1 and PowerShell 7. Run the generated `.ps1` with Rukbat source files removed and no compiler dependency.
- Confirm the example source contains no `powershell<T>` escape hatch. A test-only checker fixture may use PowerShell; it is not part of the Rukbat example implementation.
