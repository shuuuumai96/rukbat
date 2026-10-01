Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Utf8 = [Text.UTF8Encoding]::new($false, $true)

function Assert-OperationsHost {
    if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') { throw 'FullLanguage is required.' }
    if ([Environment]::OSVersion.Platform -ne 'Win32NT') { throw 'Windows is required.' }
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
}

function Read-JsonFile([string]$Path) {
    $value = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($Path, $script:Utf8))
    # PowerShell 7 may deserialize ISO timestamps as DateTime; 5.1 leaves them as strings.
    foreach ($property in $value.PSObject.Properties) {
        if ($property.Value -is [datetime]) { $property.Value = $property.Value.ToUniversalTime().ToString('o') }
    }
    return $value
}

function Write-AtomicJson([string]$Path, $Value) {
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $bytes = $script:Utf8.GetBytes((ConvertTo-Json -InputObject $Value -Depth 12))
        $stream = [IO.File]::Open($temporary, 'CreateNew', 'Write', 'None')
        try { $stream.Write($bytes, 0, $bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary, $Path, [System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($temporary, $Path) }
    } finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
}

function Get-Sha256([string]$Path) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
}

function Assert-LocalPath([string]$Path, [string]$Kind) {
    if ($Path -notmatch '^[A-Za-z]:[\\/]' -or $Path.IndexOf(':', 2) -ge 0) { throw "Expected an absolute local path: $Path" }
    $full = [IO.Path]::GetFullPath($Path)
    if ($Kind -eq 'Directory' -and -not [IO.Directory]::Exists($full)) { throw "Directory does not exist: $full" }
    if ($Kind -eq 'File' -and -not [IO.File]::Exists($full)) { throw "File does not exist: $full" }
    $cursor = $full
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse paths are not supported: $cursor" }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    return $full
}

function Read-JobConfig([string]$Path) {
    $config = Read-JsonFile (Assert-LocalPath $Path File)
    $names = @('schema_version','job_id','release_directory','working_directory','state_directory','powershell','arguments','timeout_seconds','max_attempts','retry_exit_codes','retry_delay_seconds','first_due_utc','max_success_age_seconds')
    foreach ($name in $names) { if ($null -eq $config.PSObject.Properties[$name]) { throw "Missing configuration field: $name" } }
    foreach ($property in $config.PSObject.Properties) { if ($property.Name -notin $names) { throw "Unknown configuration field: $($property.Name)" } }
    if ($config.schema_version -ne 1 -or $config.job_id -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$') { throw 'Invalid schema version or job ID.' }
    if ($config.job_id -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$') { throw 'Job ID must not be a reserved Windows device name.' }
    foreach ($name in @('timeout_seconds','max_attempts','retry_delay_seconds','max_success_age_seconds')) {
        if ($config.$name -isnot [int] -and $config.$name -isnot [long]) { throw "Expected integer: $name" }
    }
    if ($config.timeout_seconds -lt 1 -or $config.timeout_seconds -gt 86400 -or $config.max_attempts -lt 1 -or $config.max_attempts -gt 10 -or $config.retry_delay_seconds -lt 0 -or $config.retry_delay_seconds -gt 3600 -or $config.max_success_age_seconds -lt 1) { throw 'Invalid time limit or retry count.' }
    if ($config.arguments -isnot [array] -or $config.retry_exit_codes -isnot [array]) { throw 'Arguments and retry exit codes must be arrays.' }
    foreach ($argument in $config.arguments) { if ($argument -isnot [string] -or $argument.Contains([string][char]0)) { throw 'Arguments must be strings without NUL.' } }
    foreach ($code in $config.retry_exit_codes) { if (($code -isnot [int] -and $code -isnot [long]) -or $code -lt 1 -or $code -gt [int]::MaxValue) { throw 'Retry exit codes must be positive integers.' } }
    if ($config.first_due_utc -notmatch 'Z$') { throw 'first_due_utc must be an ISO timestamp ending in Z.' }
    [void][DateTimeOffset]::Parse($config.first_due_utc, [Globalization.CultureInfo]::InvariantCulture)
    foreach ($name in @('release_directory','working_directory','state_directory')) { $config.$name = Assert-LocalPath $config.$name Directory }
    $stateDrive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($config.state_directory))
    if ($stateDrive.DriveType -ne [IO.DriveType]::Fixed -or $stateDrive.DriveFormat -ne 'NTFS') { throw 'State storage must be on a local fixed NTFS volume.' }
    $config.powershell = Assert-LocalPath $config.powershell File
    if ([IO.Path]::GetFileName($config.powershell) -notin @('powershell.exe','pwsh.exe')) { throw 'powershell must name powershell.exe or pwsh.exe.' }
    return $config
}

function Open-JobLock($Config) {
    return [IO.File]::Open((Join-Path $Config.state_directory ($Config.job_id + '.lock')), 'OpenOrCreate', 'ReadWrite', 'None')
}

function Get-StatePath($Config) { return (Join-Path $Config.state_directory ($Config.job_id + '.json')) }

function Read-RunRecord($Config) {
    $record = Read-JsonFile (Get-StatePath $Config)
    foreach ($name in @('schema_version','job_id','run_id','config_sha256','status','started_utc','finished_utc','last_success_utc')) {
        if ($null -eq $record.PSObject.Properties[$name]) { throw "Invalid run record: missing $name" }
    }
    if ($record.schema_version -ne 1 -or $record.job_id -cne $Config.job_id -or $record.run_id -cnotmatch '^[a-f0-9]{32}$' -or $record.config_sha256 -cnotmatch '^[a-f0-9]{64}$' -or $record.status -notin @('running','succeeded','failed','unknown','timed_out')) { throw 'Invalid run record identity or status.' }
    foreach ($name in @('started_utc','finished_utc','last_success_utc')) {
        if ($record.$name) {
            if ([DateTimeOffset]::Parse($record.$name) -gt [DateTimeOffset]::UtcNow) { throw 'Run record is in the future; check the system clock.' }
        }
    }
    if (-not $record.started_utc -or ($record.status -eq 'succeeded' -and (-not $record.finished_utc -or -not $record.last_success_utc))) { throw 'Run record is missing completion timestamps.' }
    return $record
}

function Save-Run($Config, $Record) {
    Write-AtomicJson (Join-Path $Config.state_directory ($Record.run_id + '.json')) $Record
    Write-AtomicJson (Get-StatePath $Config) $Record
}

function Open-Release($Config) {
    $held = [Collections.Generic.List[IO.FileStream]]::new()
    try {
        $manifestPath = Join-Path $Config.release_directory 'release.json'
        $held.Add([IO.File]::Open($manifestPath, 'Open', 'Read', 'Read'))
        $manifest = Read-JsonFile $manifestPath
        if ($manifest.schema_version -ne 1 -or $manifest.entry -ne 'job.ps1') { throw 'Unsupported release manifest.' }
        $required = @('job.ps1','Common.ps1','Invoke-Job.ps1','Test-Job.ps1','Manage-Task.ps1','ProcessJob.cs.txt')
        $actual = @($manifest.files.PSObject.Properties.Name)
        if (@(Compare-Object $required $actual).Count -ne 0) { throw 'Unexpected release file list.' }
        foreach ($name in $required) {
            $path = Assert-LocalPath (Join-Path $Config.release_directory $name) File
            $held.Add([IO.File]::Open($path, 'Open', 'Read', 'Read'))
            if ((Get-Sha256 $path) -cne $manifest.files.$name) { throw "Release checksum mismatch: $name" }
        }
        if ([IO.Path]::GetFullPath($PSScriptRoot) -ne $Config.release_directory) { throw 'Invoke the tools from the configured release directory.' }
        return [pscustomobject]@{ Manifest = $manifest; Handles = $held }
    } catch { foreach ($handle in $held) { $handle.Dispose() }; throw }
}

function Import-ProcessJob {
    if ($null -eq ('RukbatProcessJob' -as [type])) { Add-Type -TypeDefinition ([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ProcessJob.cs.txt'))) }
}

function Get-EncodedInvocation([string]$Script, [string[]]$Arguments) {
    # Encode PowerShell literals, not interpolated shell command text. Preserve empty arguments.
    $literals = @($Arguments | ForEach-Object { "'" + $_.Replace("'", "''") + "'" })
    $command = "[Console]::OutputEncoding = [Text.UTF8Encoding]::new(`$false); & '" + $Script.Replace("'", "''") + "' " + ($literals -join ' ')
    return ('-NoLogo -NoProfile -NonInteractive -EncodedCommand ' + [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command)))
}
