# Rukbat on Windows

## VS Code: install one extension

Install `rukbat-<version>-win32-x64.vsix` using **Extensions: Install from VSIX**.
Open a saved `.rukbat` file in a trusted Windows workspace. Diagnostics,
completion and formatting use the compiler included in the extension.
No .NET SDK/runtime installation, PATH change, compiler configuration or
additional download is required. The first release targets Windows x64.
Existing application-control policy still applies to the bundled executable.

Use the editor title actions or Command Palette:

- **Rukbat: Check File** checks the active file.
- **Rukbat: Test Project** discovers tests under its workspace folder (or its directory if opened outside a workspace).
- **Rukbat: Build PowerShell** writes a `.ps1` beside the active file and displays its path.
- **Rukbat: Run File** builds and runs it with Windows PowerShell. Enter arguments as a JSON string array, such as `["config.json", "report.json"]`; each string is passed literally as one argument.
- **Rukbat: Check Environment** checks the local PowerShell/native-helper environment.

Commands save open file-backed documents first; canceling a save stops the command.
The working directory is the active file's workspace folder, or its parent directory
outside a workspace. Commands run as VS Code tasks, retaining output and exit codes
in the task terminal. Compilation diagnostics also appear in Problems.
Workspace trust is required for all compiler commands. Opening a file does not run it.

Leave `rukbat.server.command` empty to use the bundled compiler. An explicit command
and arguments select an external compiler for both LSP and editor actions; this is
an optional development override. Unsupported hosts need an external compiler;
generated-script execution remains a Windows operation.

## CLI: extract one ZIP

Extract `rukbat-<version>-win-x64.zip` to a writable location. Keep the entire
directory together; the executable uses the adjacent bundled runtime and libraries.
Open a terminal in that directory:

```powershell
.\rukbat.exe --version
.\rukbat.exe doctor
.\rukbat.exe check .\example\hello.rukbat
.\rukbat.exe test .\example
.\rukbat.exe build .\example\hello.rukbat
.\rukbat.exe run .\example\hello.rukbat
```

No administrator access, .NET installation or network access is required to use
the package. PATH registration is optional. Explicit paths also work in automated
builds: `C:\Tools\Rukbat\0.1.0\rukbat.exe build C:\Jobs\main.rukbat`.
Generated scripts run separately using Windows PowerShell 5.1; they do not need
the compiler, extension or the bundled runtime. Native APIs require FullLanguage
and Add-Type permission. HTTP requires the target's .NET Framework 4.7.1 or later.
Process containment requires Windows 10 / Server 2016 or later.

`--version` prints the compiler version and returns 0. `doctor` prints the compiler
location, OS/architecture, Windows PowerShell version/path, language mode and native
helper/HTTP capability results. It returns 0 when those checks pass, or 2 on a
missing/blocked capability or probe failure. Its probe is bounded to 15 seconds,
uses no profile and makes no policy changes. It does not certify script execution
policy, per-job permissions, external services or scheduler configuration.

## Offline use and controlled updates

Transfer the VSIX/ZIP and `SHA256SUMS` through your organization's approved route.
Compare hashes with a trusted copy of SHA256SUMS before installing. A hash manifest
detects changed files; it does not establish publisher identity by itself.
Archives have versioned names. Keep the approved ZIP in a versioned directory and
pin automated builds to its executable. To roll back, select the previous directory
or install the previous VSIX. VS Code's automatic extension-update setting controls
editor updates if a Marketplace version is used in the future.

## Build the distribution (maintainers)

Development requires the .NET 8 SDK and Node.js/npm. From the repository root:

```powershell
.\tools\package.ps1
# Optional: choose a new destination directory.
.\tools\package.ps1 -Destination .\artifacts\approved-build
```

The script publishes an untrimmed self-contained win-x64 compiler, checks its
version against the extension, bundles that exact compiler into the Windows VSIX,
and creates a CLI ZIP and SHA256SUMS. Dependency restoration needs network access
unless caches are complete. Existing destination directories are refused. A failed
build may leave incomplete output; only a successful run produces a complete release.
The extension runtime directory is generated and ignored by Git.

`rukbat doc input.rukbat` generates offline HTML; `--check` validates comments and
opt-in examples without writing output. The CLI and VSIX bundle the Markdown
parser and its license notice. No extra tool installation is required.

Before approving a release, exercise check/build/run/test/fmt/doc/LSP from the ZIP and
VSIX with no system .NET dependency. A clean Windows machine with no SDK/runtime
and no network must also be used for final acceptance. Verify the generated `.ps1`
on a separate target without the compiler and retain previous approved packages.

After extracting the ZIP to a fresh directory, `node tools/test-distribution.js
<extracted-directory>` verifies the included runtime configuration, removes .NET
from the child PATH, redirects DOTNET_ROOT to a nonexistent directory, and exercises
all CLI commands and LSP. It also runs generated scripts after deleting their sources
on both installed PowerShell hosts. This controlled test does not replace clean-PC
acceptance. Editor acceptance can use the extracted VSIX as described in the extension
README, without installing over an existing user extension.
