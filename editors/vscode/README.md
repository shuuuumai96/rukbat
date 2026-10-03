# Rukbat for VS Code

Provides `.rukbat` highlighting, `//` commenting, brackets, snippets, compiler
diagnostics, completion, hover, go to definition, and canonical formatting.

The compiler must be installed separately. The extension launches `rukbat lsp`
by default. For a local checkout, build it with `dotnet build Rukbat.sln`, then
configure these VS Code settings with the actual absolute DLL path:

```json
{
    "rukbat.server.command": "dotnet",
    "rukbat.server.arguments": ["C:/path/to/Auvil/src/Rukbat/bin/Debug/net8.0/rukbat.dll"]
}
```

Run `npm ci`, then `npm run package` in this directory. Install
`artifacts/rukbat.vsix` through **Extensions: Install from VSIX**. Alternatively,
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
build, set `RUKBAT_TEST_DLL` to its absolute path.

References, rename, signature help, and inferred-type hints are not implemented
in this initial version. The server currently supports file-backed documents;
save a new untitled document before using language-server features.
