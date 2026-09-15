# Practical CLI acceptance scenario: batch file validation

## Status and purpose

This is the acceptance contract for the next example, not an implemented CLI or a claim of a validated personal workflow.
It exercises a common automation sequence: read typed JSON configuration, find files recursively, invoke an existing checker for each file, save a typed JSON report, and return an explicit exit code.
The checker is a user-supplied executable already available on the target machine; it is not a new Rukbat dependency.

Implementation order is: define this scenario, add only the missing standard-library APIs below, then complete the example and its end-to-end tests.
Keep these changes separate from the completed CLI foundation and from each other.

## Invocation and configuration

Planned invocation:

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

`directory`, `pattern`, and `executable` must be nonempty. Decode with `json.decode<Config>` and its existing strict type rules.
For each matching file, copy the configured arguments, append the file's absolute path as one argument, and call `process.run`.
Do not build a shell command string or interpolate the path into code. Run checkers sequentially.
A checker exit code of zero means that file passed. Any nonzero checker code means it failed, but does not stop checks of the remaining files.

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
Save only after all checks finish, using `files.write` and `json.encode`: UTF-8 without a BOM or trailing newline.
An existing report is replaced on completed runs, including runs with failed checks. Replacement is not atomic under the current `files.write` contract.

On a normal run, stdout is empty; checker streams belong only in the report. Operational errors produce an English diagnostic through `stderr.print`.

| Program exit code | Meaning | Report behavior |
|---|---|---|
| `0` | All checks passed, no files matched, or help was requested. | Saved for a normal run; untouched for help. |
| `1` | At least one checker returned nonzero. | Saved with all completed results. |
| `2` | Invalid arguments/configuration, enumeration failure, process API Error, or report-write failure. | No write before the final save; a save failure can leave partial output. |

Enumeration completes before starting any checker. A process API Error stops the run rather than inventing a checker exit code or writing a partial report.
Checker side effects cannot be rolled back. This example is intended for read-only validators; executable selection remains the caller's responsibility.

## Minimum standard-library additions

This is a proposed contract to implement in a subsequent commit. It is not yet part of the implemented API in STDLIB.md.

| Proposed API | Required behavior and reason |
|---|---|
| `files.list_recursive(directory: string, pattern: string): string[]` | Return absolute paths to matching files at every depth, including the root's direct files. A file path, nonexistent/inaccessible root, invalid pattern, or traversal failure causes Error, not a partial result. Paths are literal; the file-name pattern uses the same matching rules as `files.list`. Exclude hidden files and hidden subtrees. Skip reparse-point entries rather than following links or junctions; reject a reparse-point root. Do not guarantee order. Needed because `files.list` cannot enumerate subdirectories. |

This API must remain standalone and compatible with Windows PowerShell 5.1. Runtime failures use ordinary Rukbat Error.
Existing `args`, arrays/append, control flow, `files.read/write`, `json.decode/encode`, `process.run`, `stderr.print`, and `exit` cover the rest.
The recursive listing already returns absolute paths, so this scenario does not require a separate `path.absolute` API. It also does not yet justify copy, move, remove, directory creation, parent-path lookup, additional text operations, CSV, or a new test runner.

## Acceptance tests for the completed example

- Check nested files and direct children while excluding nonmatches and directories. Confirm each file is passed once as a separate final argument.
- Cover no matches and one match; assert JSON array shape and BOM-free output without a trailing newline.
- Cover spaces, brackets, non-ASCII filenames, and shell metacharacters in paths and arguments. Verify that configured arguments do not accumulate file paths across iterations.
- Use a deterministic test checker to produce successful and unsuccessful results, multiline UTF-8 stdout/stderr, and empty streams. Verify captured values and the program exit code; mixed results must include all files.
- Reject missing/extra/empty arguments and malformed or incorrectly typed configuration. Verify stderr and code `2`, with an existing report unchanged.
- Cover missing roots, file-as-root, inaccessible traversal where test permissions allow it, invalid patterns, and a missing executable. Do not silently skip unavailable permission fixtures: report their limitations.
- Cover hidden files/subtrees and reparse-point handling without following a directory cycle. Tests requiring link-creation privileges must report unavailable fixtures explicitly.
- Verify a failed report save is reported as failure, not success; do not promise preservation after a write begins.
- Verify relative paths from a working directory different from the configuration directory.
- Compile and run the example on Windows PowerShell 5.1 and PowerShell 7. Run the generated `.ps1` with Rukbat source files removed and no compiler dependency.
- Confirm the example source contains no `powershell<T>` escape hatch. A test-only checker fixture may use PowerShell; it is not part of the Rukbat example implementation.
