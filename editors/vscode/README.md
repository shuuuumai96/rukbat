# Rukbat for VS Code

Provides `.rukbat` highlighting, `//` commenting, brackets, snippets, compiler
diagnostics, completion, hover, go to definition, and canonical formatting.

The Windows x64 VSIX includes its matching self-contained compiler and LSP.
Install it using **Extensions: Install from VSIX**, then open a saved `.rukbat`
file in a trusted workspace. No separate compiler, .NET installation, PATH
configuration or download is needed. See `docs/DISTRIBUTION.md` in the repository or CLI ZIP
for offline installation, CLI packages and controlled updates.

Editor title actions and the Command Palette provide **Check File**, **Test Project**,
**Build PowerShell**, **Run File** and **Check Environment**. Tasks save files first
and display results in the task terminal. Run accepts literal arguments as a JSON
string array. Tests run under the active file's workspace folder or directory.

The Command Palette also provides **Rukbat: Generate Documentation**. It saves
files and creates an offline `.html` beside the active source, including reachable
user imports. `//!` module comments and `///` declaration comments appear in hover;
completion shows the first paragraph. Documentation problems are warnings and do
not prevent ordinary compilation. Explicitly checked code examples are validated
by the documentation command only. Use the `doc` and `module_doc` snippets to start
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

References, rename, signature help, and inferred-type hints are not implemented
in this initial version. The server currently supports file-backed documents;
save a new untitled document before using language-server features.
