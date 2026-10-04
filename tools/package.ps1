[CmdletBinding()]
param([string]$Destination)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$extension = Join-Path $repo 'editors\vscode'
$metadata = Get-Content (Join-Path $extension 'package.json') -Raw | ConvertFrom-Json
$version = $metadata.version
if (-not $Destination) {
    $Destination = Join-Path $repo ('artifacts\distribution\' + $version + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$output = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $output) { throw "Destination already exists: $output. Choose a new directory." }
$portable = Join-Path $output 'portable'
$runtime = Join-Path $extension 'runtime\win-x64'
$expectedRuntime = [IO.Path]::GetFullPath((Join-Path $repo 'editors\vscode\runtime\win-x64'))
if ([IO.Path]::GetFullPath($runtime) -ne $expectedRuntime) { throw 'Unexpected runtime path.' }
if ((Test-Path -LiteralPath $runtime) -and -not (Test-Path -LiteralPath (Join-Path $runtime 'distribution.json'))) {
    throw 'Refusing to replace an unrecognized extension runtime directory.'
}
New-Item -ItemType Directory -Path $portable -Force | Out-Null
Push-Location $repo
try {
    & dotnet publish src/Rukbat/Rukbat.csproj --configfile (Join-Path $repo 'NuGet.Config') -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false -o $portable
    if ($LASTEXITCODE -ne 0) { throw 'Compiler publishing failed. No distribution is complete.' }
    $actual = & (Join-Path $portable 'rukbat.exe') --version
    if ($LASTEXITCODE -ne 0 -or $actual -ne "rukbat $version") { throw "Compiler/extension version mismatch: $actual" }
    $manifest = [ordered]@{ version = $version; runtime = 'win-x64'; self_contained = $true }
    [IO.File]::WriteAllText((Join-Path $portable 'distribution.json'), ($manifest | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $runtime) { Remove-Item -LiteralPath $expectedRuntime -Recurse -Force }
    New-Item -ItemType Directory -Path $runtime -Force | Out-Null
    Copy-Item -Path (Join-Path $portable '*') -Destination $runtime -Recurse
    Copy-Item -LiteralPath (Join-Path $repo 'docs\DISTRIBUTION.md') -Destination (Join-Path $portable 'README.md')
    Copy-Item -LiteralPath (Join-Path $repo 'docs') -Destination (Join-Path $portable 'docs') -Recurse
    New-Item -ItemType Directory -Path (Join-Path $portable 'example') | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'examples\distribution\hello.rukbat'), (Join-Path $repo 'examples\distribution\test_hello.rukbat') -Destination (Join-Path $portable 'example')
    Push-Location $extension
    try {
        & npm.cmd ci
        if ($LASTEXITCODE -ne 0) { throw 'Extension dependency installation failed.' }
        & npm.cmd run package
        if ($LASTEXITCODE -ne 0) { throw 'Extension packaging failed.' }
    } finally { Pop-Location }
    $vsixName = "rukbat-$version-win32-x64.vsix"
    Copy-Item -LiteralPath (Join-Path $extension "artifacts\$vsixName") -Destination $output
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = Join-Path $output "rukbat-$version-win-x64.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory($portable, $zip)
    $checksums = @($zip, (Join-Path $output $vsixName)) | ForEach-Object {
        (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_)
    }
    [IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS'), $checksums, (New-Object Text.UTF8Encoding($false)))
    Write-Output "Distribution created: $output"
} finally { Pop-Location }
