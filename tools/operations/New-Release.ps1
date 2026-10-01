[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$CompilerDll,
    [Parameter(Mandatory=$true)][string]$Destination,
    [Parameter(Mandatory=$true)][ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,79}$')][string]$ReleaseId
)
. (Join-Path $PSScriptRoot 'Common.ps1')
try {
    Assert-OperationsHost
    $Source = Assert-LocalPath $Source File
    $CompilerDll = Assert-LocalPath $CompilerDll File
    $Destination = Assert-LocalPath $Destination New
    if (Test-Path -LiteralPath $Destination) { throw 'Release destination must not exist. Use a new version directory.' }
    & dotnet $CompilerDll build $Source
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE); no release was created." }
    $artifact = [IO.Path]::ChangeExtension($Source, '.ps1')
    [void][IO.Directory]::CreateDirectory($Destination)
    Copy-Item -LiteralPath $artifact -Destination (Join-Path $Destination 'job.ps1')
    foreach ($name in @('Common.ps1','Invoke-Job.ps1','Test-Job.ps1','Manage-Task.ps1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $Destination $name)
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../../src/Rukbat/Runtime/ProcessJob.cs.txt') -Destination (Join-Path $Destination 'ProcessJob.cs.txt')
    $hashes = [ordered]@{}
    foreach ($name in @('job.ps1','Common.ps1','Invoke-Job.ps1','Test-Job.ps1','Manage-Task.ps1','ProcessJob.cs.txt')) { $hashes[$name] = Get-Sha256 (Join-Path $Destination $name) }
    Write-AtomicJson (Join-Path $Destination 'release.json') ([ordered]@{
        schema_version = 1; release_id = $ReleaseId; entry = 'job.ps1'; built_utc = [DateTimeOffset]::UtcNow.ToString('o')
        compiler_sha256 = Get-Sha256 $CompilerDll; files = $hashes
    })
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 2 }
