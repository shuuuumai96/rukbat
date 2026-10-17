# Rukbat

[日本語](docs/ja/README.md) · [Language](docs/language.md) · [Standard library](docs/STDLIB.md) · [Examples](examples/)

Rukbat is a small, statically typed language for Windows system scripting.
It compiles to standalone Windows PowerShell 5.1 scripts. No Rukbat installation
is needed on the target machine.

```rukbat
fn greet(name: string): void {
    print("Hello, " + name)
}

greet("Rukbat")
```

## Getting started

No packaged release is available yet. Build the Windows x64 VSIX and CLI ZIP
using the [packaging instructions](docs/DISTRIBUTION.md#build-the-distribution-maintainers).

In VS Code, install the VSIX using **Extensions: Install from VSIX**.
Open a trusted folder, save the example as `hello.rukbat`, then run
**Rukbat: Run File** from the Command Palette (accept `[]` for no arguments).
Use **Rukbat: Build PowerShell** to generate `hello.ps1` beside the source.
The extension includes the compiler, completion, diagnostics, formatting and debugging.

For terminal use, extract the CLI ZIP and run:

```powershell
.\rukbat.exe check .\hello.rukbat
.\rukbat.exe run .\hello.rukbat
.\rukbat.exe build .\hello.rukbat
powershell -NoProfile -File .\hello.ps1
```

The packages include .NET; no separate runtime installation is needed.
Generated scripts require FullLanguage mode; native helpers require Add-Type permission.
See the [distribution guide](docs/DISTRIBUTION.md) for environment requirements.

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

<details>
<summary>Build and test from source</summary>

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

</details>
