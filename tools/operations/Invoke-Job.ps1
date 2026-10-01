[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ConfigPath, [string]$RetryRunId, [string]$RecoveryNote)
. (Join-Path $PSScriptRoot 'Common.ps1')
$lock = $null
$release = $null
$record = $null
$configHandle = $null
$exitCode = 2
try {
    Assert-OperationsHost
    $ConfigPath = Assert-LocalPath $ConfigPath File
    $configHandle = [IO.File]::Open($ConfigPath, 'Open', 'Read', 'Read')
    $config = Read-JobConfig $ConfigPath
    $configHash = Get-Sha256 $ConfigPath
    $lock = Open-JobLock $config
    $release = Open-Release $config
    $previous = $null
    $statePath = Get-StatePath $config
    if ([IO.File]::Exists($statePath)) { $previous = Read-RunRecord $config }
    if ($null -ne $previous -and $previous.status -ne 'succeeded') {
        if ($RetryRunId -cne $previous.run_id -or [string]::IsNullOrWhiteSpace($RecoveryNote)) { throw "Previous run requires reconciliation. Use -RetryRunId '$($previous.run_id)' -RecoveryNote after inspecting its effects." }
        if ($previous.config_sha256 -cne $configHash) { throw 'Retry configuration differs from the unresolved run. Restore it before reconciliation.' }
    } elseif ($RetryRunId) { throw 'RetryRunId does not identify an unresolved run.' }
    $record = [ordered]@{
        schema_version = 1; job_id = $config.job_id; run_id = [guid]::NewGuid().ToString('N')
        release_id = $release.Manifest.release_id; artifact_sha256 = $release.Manifest.files.'job.ps1'
        config_sha256 = $configHash; started_utc = [DateTimeOffset]::UtcNow.ToString('o'); finished_utc = $null
        status = 'running'; child_exit_code = $null; attempts = 0; diagnostic = $null
        previous_run_id = $(if ($previous) { $previous.run_id } else { $null })
        recovery_note = $RecoveryNote; last_success_utc = $(if ($previous) { $previous.last_success_utc } else { $null })
    }
    Save-Run $config $record
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $deadline = $config.timeout_seconds * 1000
    Import-ProcessJob
    while ($record.attempts -lt $config.max_attempts) {
        $remaining = $deadline - $timer.ElapsedMilliseconds
        if ($remaining -le 0) { throw [TimeoutException]::new('Job deadline expired.') }
        $record.attempts++
        Save-Run $config $record
        $command = Get-EncodedInvocation (Join-Path $config.release_directory 'job.ps1') $config.arguments
        $result = [RukbatProcessJob]::Run($config.powershell, $command, $config.working_directory, [int]$remaining)
        $prefix = Join-Path $config.state_directory ($record.run_id + '.' + $record.attempts)
        [IO.File]::WriteAllText($prefix + '.stdout.log', [string]$result[1], $script:Utf8)
        [IO.File]::WriteAllText($prefix + '.stderr.log', [string]$result[2], $script:Utf8)
        $record.child_exit_code = [int]$result[0]
        if ($result[0] -eq 0) { $record.status = 'succeeded'; $exitCode = 0; break }
        $record.status = 'failed'
        $exitCode = 1
        if ($record.attempts -ge $config.max_attempts -or $result[0] -notin $config.retry_exit_codes) { break }
        $record.status = 'running'
        Save-Run $config $record
        $delay = [Math]::Min($config.retry_delay_seconds * 1000, [Math]::Max(0, $deadline - $timer.ElapsedMilliseconds))
        Start-Sleep -Milliseconds ([int]$delay)
    }
} catch {
    $failure = $_.Exception
    while ($null -ne $failure.InnerException) { $failure = $failure.InnerException }
    [Console]::Error.WriteLine($failure.Message)
    if ($null -ne $record) {
        $record.status = $(if ($failure -is [TimeoutException]) { 'timed_out' } else { 'unknown' })
        $record.diagnostic = $failure.Message
        $exitCode = 1
    }
} finally {
    try {
        if ($null -ne $record) {
            $record.finished_utc = [DateTimeOffset]::UtcNow.ToString('o')
            if ($record.status -eq 'succeeded') { $record.last_success_utc = $record.finished_utc }
            Save-Run $config $record
        }
    } catch { [Console]::Error.WriteLine("Could not persist result: $($_.Exception.Message)"); $exitCode = 2 }
    if ($null -ne $release) { foreach ($handle in $release.Handles) { $handle.Dispose() } }
    if ($null -ne $lock) { $lock.Dispose() }
    if ($null -ne $configHandle) { $configHandle.Dispose() }
}
exit $exitCode
