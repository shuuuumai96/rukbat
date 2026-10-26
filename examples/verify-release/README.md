# Verify release artifacts

Download the ZIP, VSIX and `SHA256SUMS` from the same release into a directory
such as `release`. From the repository root, check the exact bytes of each listed file:

```powershell
rukbat run examples/verify-release/main.rukbat -- ./release
```

This example reads the manifest produced by `tools/package.ps1`: a lowercase
SHA-256 digest, two spaces, and a filename without directories. Empty lines are
ignored. Filenames containing two consecutive spaces are not supported.
Paths are relative to the working directory. Files are read without modification.

Exit codes: `0` means all listed files match, `1` means a digest mismatch, and `2`
means invalid input or a read failure (including missing files or an empty manifest).
Extra files not listed in the manifest are not checked. Use a trusted manifest;
matching hashes alone do not establish publisher identity.

This source example is available in the repository after the 0.1.0 release;
it is not included in that release's source archive or binary packages.
It works with the 0.1.0 compiler.

To run without the compiler, build once, then run the generated script:

```powershell
rukbat build examples/verify-release/main.rukbat
powershell -NoProfile -File examples/verify-release/main.ps1 ./release
```
