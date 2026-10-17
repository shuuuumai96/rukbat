# Batch file validation example

The [batch-check example](../examples/batch-check/main.rukbat) reads
JSON configuration, finds files recursively, invokes a user-supplied checker, and
writes a JSON report.

## Invocation and configuration

```powershell
rukbat run examples/batch-check/main.rukbat -- config.json report.json
```

Exactly two nonempty positional arguments are required. A sole `--help` prints usage to stdout and exits with code `0` without reading files or launching a checker.
All relative paths, including paths in the configuration, resolve against the process working directory, not the configuration file's directory.
The report must be outside the input directory tree and its parent directory must already exist.

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

`directory`, `pattern`, and `executable` must be nonempty. Configuration is validated with `json.parse<Config>`.
The example runs checkers sequentially with a 30-second timeout per file. Each invocation receives the configured arguments followed by the file's absolute path as one literal argument. The checker must produce UTF-8 output.
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

Each `file` is an absolute path. Exit codes and captured stdout/stderr are preserved, including trailing newlines. Report order follows enumeration order and is unspecified.
No matches produce `[]` without launching a checker. One match still produces an array.
The report is saved only after all checks finish, as UTF-8 without a BOM or trailing newline.
An existing report is replaced on completed runs, including runs with failed checks. `fs.write_text` stages complete encoded report contents in the report directory before the final replacement; a conversion failure or failure before that step leaves an existing report unchanged. The final filesystem replacement has no durability or universal atomicity guarantee.

On a normal run, stdout is empty; checker streams belong only in the report. Operational errors produce an English diagnostic through `eprint`.

| Program exit code | Meaning | Report behavior |
|---|---|---|
| `0` | All checks passed, no files matched, or help was requested. | Saved for a normal run; untouched for help. |
| `1` | At least one checker returned nonzero. | Saved with all completed results. |
| `2` | Invalid arguments/configuration, enumeration failure, process API Error, or report-write failure. | No write before the final save; a pre-replacement save failure leaves an existing report unchanged. |

Enumeration completes before starting any checker. A process API Error stops the run rather than inventing a checker exit code or writing a partial report.
Checker side effects cannot be rolled back. This example is intended for read-only validators; executable selection remains the caller's responsibility.
