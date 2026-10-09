# Rukbat

Rukbat is a small, statically typed language for Windows system scripting.
It compiles `.rukbat` sources and their imports into one standalone `.ps1` file
for Windows PowerShell 5.1. The generated script needs no Rukbat installation.

## Getting started

On Windows x64, install `rukbat-<version>-win32-x64.vsix` in VS Code using
**Extensions: Install from VSIX**. The extension includes the compiler;
no separate .NET installation or PATH configuration is needed.

Open a trusted folder and save this as `hello.rukbat`:

```rukbat
fn greet(name: string): void {
    print("Hello, " + name)
}

greet("Rukbat")
```

1. Run **Rukbat: Run File** from the Command Palette. Accept `[]` for no arguments.
2. Run **Rukbat: Build PowerShell** to generate `hello.ps1` beside the source.
3. Run the generated script independently:

   ```powershell
   powershell -NoProfile -File .\hello.ps1
   ```

The extension also provides completion, diagnostics, formatting and debugging.
See the [extension guide](editors/vscode/README.md) for editor commands.

For terminal use, extract the CLI ZIP and run its `rukbat.exe`:

```powershell
.\rukbat.exe check .\hello.rukbat
.\rukbat.exe run .\hello.rukbat
.\rukbat.exe build .\hello.rukbat
```

Generated scripts require PowerShell's FullLanguage mode; native helpers require
Add-Type permission. See the [distribution guide](docs/DISTRIBUTION.md) for
packages, environment requirements and updates.

## Documentation

- [Language](docs/language.md) — syntax, types and command-line reference.
- [Standard library](docs/STDLIB.md) — files, CSV, JSON, processes and other APIs.
- [Examples](examples/) — runnable scripts, including [CSV filtering](examples/csv-filter/README.md),
  [category counting](examples/csv-count/README.md) and [JSON configuration](examples/json-config/README.md).
- [Command-line programs](docs/CLI.md), [testing](docs/TESTING.md) and [debugging](docs/DEBUGGING.md).
- [Source documentation](docs/DOCUMENTATION.md) and [editor features](docs/EDITOR.md).
- [Unattended execution](docs/UNATTENDED.md) and [operations](docs/OPERATIONS.md).
- [Design philosophy](docs/PHILOSOPHY.md) and [compiler architecture](docs/ARCHITECTURE.md).

## Developing the compiler

Building Rukbat itself requires the .NET 8 SDK. From the repository root:

```powershell
dotnet restore --configfile NuGet.Config
dotnet build --no-restore
dotnet test --no-restore
```

Execution tests require both Windows PowerShell 5.1 (`powershell`) and
PowerShell 7 (`pwsh`) on PATH. Dependency restoration requires network access
unless dependencies are cached.

To build the VSIX and portable CLI ZIP, install Node.js/npm and run
`tools/package.ps1`. See [packaging instructions](docs/DISTRIBUTION.md#build-the-distribution-maintainers).
