# Rukbat on Windows

The first preview is being prepared as 0.1.0. Once published, download the VSIX,
CLI ZIP and `SHA256SUMS` from [GitHub Releases](https://github.com/shuuuumai96/rukbat/releases).
See the [release notes](releases/0.1.0.md) for preview compatibility and limitations.

## VS Code: install one extension

Install `rukbat-<version>-win32-x64.vsix` using **Extensions: Install from VSIX**.
Open a saved `.rukbat` file in a trusted Windows workspace. Diagnostics,
completion and formatting use the compiler included in the extension.
No .NET SDK/runtime installation, PATH change, compiler configuration or
additional download is required. The package targets Windows x64.
Existing application-control policy still applies to the bundled executable.

Use **Rukbat: Run File** to run a saved source, or **Rukbat: Build PowerShell**
to write a `.ps1` beside it. Run arguments are literal JSON strings, such as
`["config.json", "report.json"]`. Commands save files first; canceling a save
stops the command. The working directory is the active file's workspace folder,
or its directory outside a workspace. Output/exit codes appear in the task terminal
and compiler diagnostics in Problems. Opening a file does not execute it.

Leave `rukbat.server.command` empty for the bundled compiler. Unsupported hosts
need an external compiler; execution remains a Windows operation. See the
[extension guide](../editors/vscode/README.md) for all commands and development overrides.

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
the package. PATH registration is optional; automation can use the executable's absolute path.
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
or install the previous VSIX.

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

The MIT license for Rukbat and the third-party notices are included in both
packages. The extension's root `LICENSE` is copied from the repository by its
packaging script. The Markdown parser is also included; source documentation
generation requires no additional installation.

The English and Japanese style guides are included at `docs/STYLE.md` and
`docs/ja/STYLE.md` in the CLI ZIP. In the VSIX, the same documentation is under
`extension/runtime/win-x64/docs/`.

### Prepare the GitHub release

Use `v0.1.0` as the first release tag and `Rukbat 0.1.0 preview` as its title.
Create a draft GitHub release, mark it as a pre-release, and use
`docs/releases/0.1.0.md` as the release body. Attach only the versioned CLI ZIP,
VSIX and `SHA256SUMS` from one successful packaging run; do not attach the
`portable` staging directory.

Record the tested source revision and complete the release acceptance below
before publishing the draft. When publishing, update the English and Japanese
READMEs and distribution guides from preparation wording to the published
release link. Building local packages does not publish a GitHub release or a
VS Code Marketplace extension.

## Release acceptance

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
