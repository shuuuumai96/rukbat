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

The first preview is being prepared as **0.1.0**. Download its Windows x64 packages
from [GitHub Releases](https://github.com/shuuuumai96/rukbat/releases) once published:

| Package | Use |
|---|---|
| `rukbat-0.1.0-win32-x64.vsix` | VS Code extension with its compiler and language server. |
| `rukbat-0.1.0-win-x64.zip` | Portable command-line compiler. |
| `SHA256SUMS` | SHA-256 hashes for both packages. |

Use the [packaging instructions](docs/DISTRIBUTION.md#build-the-distribution-maintainers)
to build these packages from source before the release is published.

In VS Code, install the VSIX using **Extensions: Install from VSIX**.
VS Code 1.82 or later is required.
Open a trusted folder, save the example as `hello.rukbat`, then run
**Rukbat: Run File** from the Command Palette (accept `[]` for no arguments).
Use **Rukbat: Build PowerShell** to generate `hello.ps1` beside the source.
The extension includes the compiler, completion, diagnostics, formatting and debugging.

For terminal use, extract the entire CLI ZIP, open a terminal in that directory,
and run the included example:

```powershell
.\rukbat.exe --version
.\rukbat.exe doctor
.\rukbat.exe check .\example\hello.rukbat
.\rukbat.exe run .\example\hello.rukbat
.\rukbat.exe build .\example\hello.rukbat
powershell -NoProfile -File .\example\hello.ps1
```

The packages include .NET; no separate runtime installation is needed.
Generated scripts require FullLanguage mode; native helpers require Add-Type permission.
See the [distribution guide](docs/DISTRIBUTION.md) for environment requirements.

## Preview status and compatibility

0.1.0 is intended for evaluation and feedback. Syntax, standard-library APIs and
tooling behavior may change in later 0.x releases. Pin the compiler version used
by automation and recheck, rebuild and test sources before upgrading.

Packages target Windows x64. Running programs requires Windows PowerShell 5.1;
PowerShell 7 is optional. Process containment requires Windows 10 / Server 2016
or later, and HTTP requires .NET Framework 4.7.1 or later on the target.
See the [0.1.0 release notes](docs/releases/0.1.0.md) for scope and limitations.

Report problems through [GitHub Issues](https://github.com/shuuuumai96/rukbat/issues),
including `rukbat --version`, `rukbat doctor`, a minimal source example, and the
expected and actual results. Remove credentials and private paths before posting.

## Documentation

- [Language](docs/language.md) — syntax, types and command-line reference.
- [Style guide](docs/STYLE.md) — naming, formatting and readable scripting conventions.
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

## License

Rukbat is available under the [MIT License](LICENSE). Bundled dependencies retain
their own licenses; see [third-party notices](docs/THIRD_PARTY_NOTICES.txt) and the
license files included in the packages.
