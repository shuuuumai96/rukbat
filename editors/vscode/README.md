# Rukbat for VS Code

Edit, check, run, and debug `.rukbat` programs on Windows. The extension provides
highlighting, completion, diagnostics, hover, navigation, rename, signature help,
and canonical formatting.

## Install and run

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
