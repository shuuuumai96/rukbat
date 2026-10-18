# Rukbat for VS Code

Edit, check, run, and debug `.rukbat` programs on Windows. The extension provides
highlighting, completion, diagnostics, hover, navigation, rename, signature help,
and canonical formatting.

## Install and run

The first preview is being prepared as 0.1.0. Once published, download
`rukbat-0.1.0-win32-x64.vsix` from
[GitHub Releases](https://github.com/shuuuumai96/rukbat/releases).
Requires VS Code 1.82 or later on Windows x64.

1. Install the Windows x64 VSIX using **Extensions: Install from VSIX**.
2. Open a trusted folder and save a `.rukbat` file.
3. Run **Rukbat: Run File**. Enter arguments as a JSON string array, or accept `[]`.

The VSIX includes its matching compiler and language server. No separate .NET
installation, PATH setup, or compiler download is needed. Leave
`rukbat.server.command` empty to use it. Reload the window after a VSIX update.

## Commands

| Command | Action |
|---|---|
| **Rukbat: Check File** | Check syntax, imports, and types. |
| **Rukbat: Test Project** | Run tests under the active file's workspace folder or directory. |
| **Rukbat: Build PowerShell** | Write a standalone `.ps1` beside the source. |
| **Rukbat: Run File** | Build and run with Windows PowerShell. |
| **Rukbat: Debug File** / F5 | Debug source with breakpoints, stepping, and variable inspection. |
| **Rukbat: Check Environment** | Check local PowerShell/native-helper capabilities. |
| **Rukbat: Generate Documentation** | Write offline HTML from source comments. |
| **Rukbat: Restart Language Server** | Restart analysis after configuration changes. |

The editor title shows **Run File** and **Debug File** as icons, in that order.
**Check File**, **Build PowerShell**, and **Test Project** are in its **More Actions**
(`...`) menu. All commands are also available in the Command Palette.

Execution and generation commands save files first. Check/build/run/test output
appears in the task terminal; debugging uses the Debug Console. Relative paths use
the active file's workspace folder, or its directory outside a workspace.
Highlighting works without workspace trust; compiler commands require trust.

See `docs/DISTRIBUTION.md`, `docs/EDITOR.md`, `docs/DEBUGGING.md`, and
`docs/DOCUMENTATION.md` in the repository or CLI ZIP for requirements, editing
scope, debugger limits, and comment syntax. Snippets include `cli`, `doc`, and
`module_doc`.

## Troubleshooting

Save untitled files before using language-server features. If startup fails,
check the **Rukbat** output channel and compiler settings, then restart the server.
Analysis uses unsaved source and imports; syntax errors can limit semantic features.

## Extension development

For a checkout, run `dotnet build Rukbat.sln` and configure the absolute DLL path:

```json
{
    "rukbat.server.command": "dotnet",
    "rukbat.server.arguments": ["C:/path/to/rukbat/src/Rukbat/bin/Debug/net8.0/rukbat.dll"]
}
```

This override applies to both LSP and editor actions. Open this directory and
launch **Rukbat Extension** to develop the extension. Run `npm ci`, `npm test`,
and `npm run test:editor` here; editor tests require a built compiler and download
VS Code into `.vscode-test` unless an executable is supplied.

| Test setting | Purpose |
|---|---|
| `RUKBAT_TEST_DLL` | Select an absolute compiler DLL path. |
| `RUKBAT_TEST_VSCODE` | Use an existing VS Code executable. |
| `RUKBAT_TEST_BUNDLED=1` | Test the bundled compiler. |
| `RUKBAT_TEST_EXTENSION` | Select an extracted VSIX's `extension` directory. |

From the repository root, `tools/package.ps1` builds the VSIX and CLI ZIP together.
`npm run package` requires a generated runtime with a matching version.

## Preview and license

Later 0.x versions may change syntax, standard-library APIs and tooling behavior.
Pin the compiler version used by automation and recheck, rebuild and test before
upgrading. See the repository's
[release notes](https://github.com/shuuuumai96/rukbat/blob/main/docs/releases/0.1.0.md)
for requirements and limitations.

Rukbat is available under the MIT License. The packaged extension includes
`LICENSE`; bundled dependencies retain their own licenses.
Report problems through [GitHub Issues](https://github.com/shuuuumai96/rukbat/issues).
