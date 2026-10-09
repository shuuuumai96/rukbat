# Testing Rukbat programs

`rukbat test` discovers ordinary functions, compiles them to standalone PowerShell,
and executes each test in its own process. Write tests next to your application or
under a `tests` directory.

## First test

Create `test_math.rukbat`:

```rukbat
import test
import "./math.rukbat" as arithmetic

fn test_add(): void {
    test.assert(arithmetic.add(2, 3) == 5)
}
```

Run from the project directory:

```powershell
rukbat test
rukbat test tests
rukbat test tests/test_math.rukbat
rukbat test --filter test_add
rukbat test --shell pwsh
rukbat test --timeout 60
```

The default path is `.`. Directory discovery recursively selects `test_*.rukbat`
files. The filename prefix and function names are case-sensitive; the extension
is case-insensitive. Discovery excludes `.git`, `bin`, `obj`, `.local`, `artifacts`,
`TestResults`, symbolic links, and junctions. Explicitly passing a `.rukbat` file
selects that file regardless of its name. An explicitly selected link is rejected.

Only top-level functions in selected files whose names start with `test_` are
collected. Imported functions and struct methods are helpers, not additional
tests. A test must return `void` and accept either no arguments or one
`test.Context` argument. A wrong signature is a collection error (`RUK5000`).
File paths and function names determine a stable ordinal execution order.
`--filter` selects names containing the given case-sensitive literal substring
of `relative/file.rukbat::test_name`; it is not a pattern or an expression.

All discovered sources, imports, and test signatures are checked before execution,
including sources excluded by the filter. All selected entrypoints also undergo
generated PowerShell syntax validation before any test runs. A collection or
compilation error prevents the entire run. An existing `.ps1` is never used or
overwritten by `test`.

## Assertions and failures

```rukbat
test.assert(result == expected)
test.assert(names.length == 2)
test.assert(text.contains(message, "ready"))
test.fail("The expected report was not created")
```

`test.assert(condition: bool): void` raises ordinary `Error` when the condition
is false. Its message includes the source expression; the Error retains the
runtime source location for failure reporting. When the complete condition is a direct primitive comparison (`==`,
`!=`, `<`, `<=`, `>`, `>=`), the message also displays the evaluated left and
right values. Operands are evaluated once in ordinary left-to-right order;
short-circuit boolean expressions keep their normal semantics. String values
are displayed with JSON quoting, and null is displayed as `<null>`.

`test.fail(message: string): void` raises ordinary `Error` with the supplied
message. Both functions accept positional arguments only. They also work in
ordinary `build`/`run` programs and generated standalone scripts.

An uncaught Error, nonzero exit, timeout, or a process exit before the test
returns normally fails that test. `exit(0)` fails too. A normal `return` from a
test succeeds if no assertion or other operation has failed. Remaining tests
continue after a failure. Success output is hidden. Failures print captured
stdout, stderr, and the failing source location; the summary lists pass/fail
counts. The runner writes UTF-8 diagnostics, including when its streams are redirected.

## Files and fixtures

```rukbat
import test
import encoding
import fs
import path

fn test_report(ctx: test.Context): void {
    input := path.join([ctx.source_dir, "fixtures", "input.txt"])
    output := path.join([ctx.temp_dir, "report.txt"])
    fs.write_text(output, fs.read_text(input, encoding.utf8), encoding.utf8)
    test.assert(fs.is_file(output))
}
```

The runner supplies `ctx.temp_dir`, an absolute empty working directory unique
to the test, and `ctx.source_dir`, the absolute directory containing its test
source file. Relative filesystem paths also resolve against `ctx.temp_dir`.
Use `ctx.source_dir` to read checked-in fixture data. `path.script_dir()` refers
to the temporary generated script directory, not the source directory.

Globals, imported module state, environment changes, and PowerShell location
changes are isolated by the separate process for each test. Each process
initializes its module graph before invoking its selected function. Keep
reusable logic in modules without application-starting top-level side effects;
importing a command entrypoint runs those side effects again for every test.
Shared setup can be an ordinary helper function called explicitly by each test.
Use `try`/`catch` for cleanup; there are no implicit fixtures or setup/teardown hooks.

Successful tests' temporary artifacts are removed without traversing directory
links. Failed tests retain their generated script and work directory; the failure
prints the artifact path under `%TEMP%/rukbat-test-...`. Normal failures also retain
`stdout.txt` and `stderr.txt`. Retained failures are not automatically pruned.

## Multiple cases and expected errors

Use ordinary arrays and loops for multiple inputs. A loop is one test: the first
failure stops it, and direct comparison diagnostics show its actual/expected values.

```rukbat
struct Case {
    input: string
    expected: int
}

fn test_parse_int(): void {
    cases: Case[] = [
        Case { input: "0", expected: 0 },
        Case { input: "-12", expected: -12 }
    ]

    for item in cases {
        test.assert(text.parse_int(item.input) == item.expected)
    }
}

fn test_reject_invalid_int(): void {
    try {
        text.parse_int("invalid")
    } catch err {
        return
    }

    test.fail("Expected Error was not thrown")
}
```

These functions require `import test` and `import text`. For an expected Error,
return from the test's catch handler and place `test.fail` after the `try`/`catch`.
This checks that an Error was thrown without depending on its message. If the
message is part of the behavior under test, optionally assert it inside the
handler before returning. Placing `test.fail` inside the `try` would cause the
handler to catch that assertion itself.

## Execution and automation

The runner requires Windows 10 / Server 2016 or later and `powershell.exe` on
PATH by default. Use `--shell pwsh` to select PowerShell 7 explicitly; a missing
host is an error, never a silent skip. Generated scripts require FullLanguage
mode and obey the host's execution policy. The runner does not change that policy.

Each test has a 30-second deadline by default, measured from process creation,
including PowerShell startup, module initialization, the test, descendants, and
output capture. `--timeout` accepts an integer from 1 through 86400 seconds.
The same native Windows Job Object helper as `process.run` contains the host and
ordinary descendants. A timeout terminates that job and testing continues after
cleanup. Cleanup can take up to five seconds for processes and five seconds for
captures. Timeout/capture failures report the cause; captured streams are not
available in those cases. stdin is closed, execution is noninteractive, and
capture is bounded to 64 MiB per stream.

Isolation does not undo external side effects or prevent access outside the
temporary directory. Services, registry changes, production files, and remote
operations retain their ordinary behavior. Use disposable inputs and explicit
test configuration for such integrations.

| Exit code | Meaning |
|---|---|
| `0` | Every selected test passed. |
| `1` | Collection/compilation failure, or at least one failed test. |
| `2` | Invalid arguments, missing host, unsupported platform, launch failure, or I/O/cleanup failure. |
| `5` | No tests collected, or no tests matched the filter. |

CI can invoke `rukbat test` and use the exit code directly. Compiler contributors
still use `dotnet test`; that suite tests the compiler and runner implementation.

The runnable [testing example](../examples/testing/test_records.rukbat) covers
CSV filtering, nested JSON updates, file output, multiple inputs, and expected
errors. From the repository root:

```powershell
.\rukbat test examples/testing
.\rukbat test examples/testing --shell pwsh
```

## Compiler contributor tests

Compiler checks and generated-PowerShell execution are separate suites. Windows
PowerShell 5.1 is the deployment target; PowerShell 7 supplies additional checks.
See the [extension guide](../editors/vscode/README.md#extension-development) for
VS Code tests.

Focused selections use `dotnet test --filter FullyQualifiedName~<name>`, for example
`Constant`, `Documentation`, or `LanguageServerTests`. Source-comment validation
uses `rukbat doc entry.rukbat --check` and never executes examples.

### Categories

| Category | Purpose |
|---|---|
| `Unit` | Lexer, parser, module loading, resolver, type checker, emitter, formatter, and non-executing CLI behavior. |
| `Runtime` | Generated-script semantics exercised on a PowerShell host. Most tests use PowerShell 7; tests also tagged `PowerShell51` exercise the primary target directly. |
| `PowerShell51` | Direct Windows PowerShell 5.1 execution, including primary-target checks. |
| `CrossVersion` | Selected public behaviors verified with both PowerShell 5.1 and PowerShell 7. |

Categories overlap: `PowerShell51` and `CrossVersion` are subsets of `Runtime`.
`Smoke=PowerShell51` is a separate trait selecting representative startup, library,
process, error, CLI, and standalone checks; every selected case executes on 5.1.

### Commands

`dotnet test` defaults to `Unit` through the VSTest filter in `Directory.Build.props`.
Use an explicit selection for execution tests:

```powershell
dotnet test -p:RukbatTestCategory=Unit
dotnet test -p:RukbatTestCategory=Runtime
dotnet test -p:RukbatTestCategory=PowerShell51
dotnet test -p:RukbatTestCategory=CrossVersion
dotnet test --filter "Smoke=PowerShell51"
dotnet test -p:RukbatTestCategory=All
```

`All` suppresses the default filter. Test parallelism and process sharing are disabled.
