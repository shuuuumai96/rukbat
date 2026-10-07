# Rukbat for VS Code

Provides `.rukbat` highlighting, `//` commenting, brackets, snippets, compiler
diagnostics, completion, hover, go to definition, references, rename, signature
help, and canonical formatting.

The Windows x64 VSIX includes its matching self-contained compiler and LSP.
Install it using **Extensions: Install from VSIX**, then open a saved `.rukbat`
file in a trusted workspace. No separate compiler, .NET installation, PATH
configuration or download is needed. See `docs/DISTRIBUTION.md` in the repository or CLI ZIP
for offline installation, CLI packages and controlled updates.

Editor title actions and the Command Palette provide **Check File**, **Test Project**,
**Build PowerShell**, **Run File** and **Check Environment**. Tasks save files first
and display results in the task terminal. Run accepts literal arguments as a JSON
string array. Tests run under the active file's workspace folder or directory.

**Rukbat: Debug File** and F5 start source-level debugging on Windows PowerShell
5.1. Set executable `.rukbat` breakpoints with F9; use F10/F11/Shift+F11 to step,
and inspect local/module variables, structs, arrays, and imported call stacks.
Files are saved before compiling. Each session builds a separate temporary
script, preserving ordinary `.ps1` output. No PowerShell extension or extra
runtime installation is required. Watches accept variable/field/index paths;
general expressions and conditional breakpoints are not supported. See
`docs/DEBUGGING.md` in the repository or CLI ZIP for launch.json arguments,
supported operations, and limits.

The Command Palette also provides **Rukbat: Generate Documentation**. It saves
files and creates an offline `.html` beside the active source, including reachable
user imports. `//!` module comments and `///` declaration comments appear in hover;
completion shows the first paragraph. Documentation problems are warnings and do
not prevent ordinary compilation. Explicitly checked code examples are validated
by the documentation command and during reference/rename requests. Use the `doc`
and `module_doc` snippets to start
comments; see `docs/DOCUMENTATION.md` for syntax and link rules.

Place `///` immediately above a function, then hover its name at either the
declaration or a call to see its signature and complete Markdown explanation:

```rukbat
/// Adds two integers.
/// Overflow raises Error.
fn add(a: int, b: int): int {
    return a + b
}

print(add(1, 2))
```

Hover also works for imported functions and unsaved comment edits. There is no
need to generate HTML first. After updating a VSIX, reload the VS Code window to
restart the bundled language server with the new compiler.

`const` declarations have keyword highlighting and a `const` snippet. Hover and
completion retain the modifier; attempted reassignment or receiver mutation is
reported as a compiler error (`RUK2011`).

Leave `rukbat.server.command` empty for the bundled compiler. For a local checkout,
build it with `dotnet build Rukbat.sln`, then
configure these VS Code settings with the actual absolute DLL path:

```json
{
    "rukbat.server.command": "dotnet",
    "rukbat.server.arguments": ["C:/path/to/Auvil/src/Rukbat/bin/Debug/net8.0/rukbat.dll"]
}
```

Run `tools/package.ps1` from the repository root to produce the complete VSIX and
CLI ZIP from the same compiler build. `npm run package` requires the generated
runtime and rejects a missing/mismatched bundle. Alternatively,
open this directory in VS Code and run the **Rukbat Extension** launch configuration.

Open a `.rukbat` file in a trusted workspace. If launching fails, use the
**Rukbat** output channel, check the two settings above, then run
**Rukbat: Restart Language Server**. Highlighting works in untrusted workspaces;
the compiler process starts only after workspace trust is granted.

The server uses in-memory edits, including open imported files. Formatting uses
the language's canonical four spaces and LF, regardless of editor formatting
options. It returns edits to the editor; it does not write files or run scripts.

Run `npm test` for TextMate grammar tests and `npm run test:editor` for tests in
a downloaded VS Code extension host. The latter needs a built compiler and
downloads VS Code into the ignored `.vscode-test` directory. To use another
build, set `RUKBAT_TEST_DLL` to its absolute path. To test a generated/extracted
VSIX using the bundled compiler, set `RUKBAT_TEST_BUNDLED=1` and optionally
`RUKBAT_TEST_EXTENSION` to its extracted `extension` directory.
`RUKBAT_TEST_VSCODE` can select an existing VS Code executable without downloading it.

Use **Find All References** to locate uses in open and closed workspace files.
The `cli` snippet supplies argument definitions, generated help and explicit error/exit handling.
See `docs/CLI.md` for value options, flags and repeatable arguments; the standard-library catalog
provides completion, field hover and parameter help for these APIs.
Use **Rename Symbol** (F2) to update a user-defined name. Changes are checked in
memory before VS Code applies them; files are not saved automatically. Rename
includes bound documentation links and explicitly checked code examples, and
refuses name capture, new compiler errors and required edits outside the workspace.
Open the project folder before renaming exported functions, structs or fields.
Standard APIs and import names, built-ins and `self` cannot be renamed. Ordinary
strings/comments, unchecked examples and PowerShell text are left to the author.

Typing `(` or `,` in a function call displays its signature and the current
parameter, including incomplete calls. Closing an inner call returns help to the
outer call. Functions, methods, standard APIs, built-ins and array `append` are
supported. Struct-literal field hints and inferred-type hints are not implemented.
See `docs/EDITOR.md` for workspace scope and rename's external-data limits.
The server supports file-backed documents; save a new untitled document before
using language-server features.
