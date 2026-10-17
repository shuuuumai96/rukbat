# Debugging Rukbat in VS Code

The extension uses Windows PowerShell 5.1 to debug `.rukbat` source. The bundled
VSIX needs no extra debugger, PowerShell extension, or .NET installation. A checkout
uses the same compiler override as its language server.

## Start a session

1. Install the matching Rukbat VSIX and reload the VS Code window.
2. Open a saved `.rukbat` entry source in a trusted workspace.
3. Set breakpoints on executable statements using the gutter or F9.
4. Press F5, choose **Rukbat** if prompted, or run **Rukbat: Debug File**.

The extension saves open files before compiling, including imported sources.
Compilation errors prevent execution. The initial stop is before the first
executable statement, which may be in an imported module's initialization.
Use F5 to continue, F10 to step over, F11 to step into, Shift+F11 to step out,
and Shift+F5 to terminate. Step operations visit Rukbat statements and skip
generated helpers and standard-library internals. A breakpoint inside a called
function can interrupt step over. Step out stops at the next executable
statement in a caller; it does not stop midway through an expression.

The Variables pane shows **Locals** and **Module** values after initialization;
arrays, dictionaries, and structs expand under their Rukbat names. Dictionaries
show insertion order and read-only length. Const values are marked read-only,
and primitives retain their types (including round-trip float precision).
The Call Stack preserves scopes for parameters, `self`, loop/catch bindings,
shadowed locals, suspended callers, and imported module initializers.
Program stdout/stderr appear in the Debug Console.

## Arguments and working directory

Create `.vscode/launch.json` when you need a fixed entry source or arguments:

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "type": "rukbat",
            "request": "launch",
            "name": "Debug Rukbat",
            "program": "${workspaceFolder}/main.rukbat",
            "cwd": "${workspaceFolder}",
            "args": ["config.json", "a literal argument with spaces"],
            "stopOnEntry": true
        }
    ]
}
```

Arguments are literal strings, never shell command text. By default the working
directory is the selected workspace folder, or the entry source directory when
no workspace folder exists. Debug scripts are created beside the entry source,
so `path.script_dir()` still identifies that directory. Each session uses its
own output and removes it after the PowerShell host exits. A regular `.ps1`
build is not overwritten. **Run Without Debugging** uses the same launch
configuration and passes arguments without stopping.

## Limitations

- Line breakpoints stop before executable statements. Declarations of functions
  or structs, imports, comments, and closing braces are not executable stops.
  Line breakpoints stay in the gutter; explicit column breakpoints use inline
  markers. If several statements share a line, its line breakpoint covers all of them.
- Watches, hovers, and the Debug Console accept read-only variable paths such as
  `count`, `self.name`, `items.length`, `items[0].name`, `users["id"].name`, or `codes[-1]`.
  Dictionary string keys accept Rukbat quotes/escapes; integer keys retain the full signed 64-bit range.
  Negative array indexes remain unavailable. General expressions, function calls,
  variable modification, conditional/hit-count/log breakpoints, exception
  breakpoints, attach, and restart requests are not implemented.
- Pause takes effect at the next Rukbat statement. It does not interrupt an
  ongoing native call or a PowerShell escape-hatch operation. Terminate stops
  the PowerShell host even while a native operation is waiting.
- Inspection shows at most 100 elements per array/dictionary, six composite levels,
  approximately 2000 value nodes per stop, and 4096 characters per string.
  Opaque standard-library values show their type without exposing backend
  properties or invoking conversion functions.
- stdin is closed; this debugger does not support interactive terminal input.
  Escape-hatch code runs normally but is not a source-level stepping target.
- Debugging adds checkpoints and changes execution timing. Use ordinary builds
  for distribution and for final operational validation.
- Source edits made during a session take effect when you start a new session.

## Compiler metadata

`rukbat build main.rukbat --debug` writes `main.ps1` and
`main.ps1.rukbat-map.json`. To preserve an existing deployment artifact, use
`rukbat build main.rukbat --debug --output session.ps1` instead. The sidecar
contains absolute development source paths, executable source/generated-line
positions, visible bound variable names, and type/field metadata. It is for
editor development only. The debug `.ps1` itself can run without the map or
debug host; its checkpoints have no external runtime dependency.

Ordinary `rukbat build` and `rukbat run` emit no debugger state or sidecar. Their
generated scripts stay standalone and Windows PowerShell 5.1-compatible.
The debugger uses PowerShell line breakpoint actions and a local named pipe
separate from the program's standard streams. It does not change execution policy.
