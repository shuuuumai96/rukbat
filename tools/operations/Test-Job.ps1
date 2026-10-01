[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ConfigPath, [string]$NotifyScript)
. (Join-Path $PSScriptRoot 'Common.ps1')
$lock = $null
$release = $null
$exitCode = 2
$report = [ordered]@{ schema_version = 1; checked_utc = [DateTimeOffset]::UtcNow.ToString('o'); job_id = $null; state = 'invalid'; run_id = $null; diagnostic = $null }
try {
    Assert-OperationsHost
    $config = Read-JobConfig $ConfigPath
    $report.job_id = $config.job_id
    $release = Open-Release $config
    $busy = $false
    try { $lock = Open-JobLock $config } catch [IO.IOException] {
        $failure = $_.Exception
        while ($failure.InnerException) { $failure = $failure.InnerException }
        $windowsError = $failure.HResult -band 0xffff
        if ($windowsError -notin @(32, 33)) { throw }
        $busy = $true
    }
    $path = Get-StatePath $config
    $now = [DateTimeOffset]::UtcNow
    if (-not [IO.File]::Exists($path)) {
        $report.state = $(if ($now -lt [DateTimeOffset]::Parse($config.first_due_utc)) { 'not_due' } else { 'missing' })
    } else {
        $record = Read-RunRecord $config
        $report.run_id = $record.run_id
        $elapsed = ($now - [DateTimeOffset]::Parse($record.started_utc)).TotalSeconds
        if ($elapsed -lt 0) { throw 'Run record is in the future; check the system clock.' }
        if ($record.status -eq 'running') {
            $report.state = $(if (-not $busy) { 'unknown' } elseif ($elapsed -gt ($config.timeout_seconds + 15)) { 'overdue' } else { 'running' })
        } elseif ($record.status -ne 'succeeded') { $report.state = $record.status }
        elseif ($record.config_sha256 -cne (Get-Sha256 $ConfigPath)) { $report.state = 'configuration_changed' }
        elseif (-not $record.last_success_utc -or ($now - [DateTimeOffset]::Parse($record.last_success_utc)).TotalSeconds -gt $config.max_success_age_seconds) { $report.state = 'stale' }
        else { $report.state = 'healthy' }
        # A currently running job must not hide a missed completion deadline.
        if ($report.state -eq 'running' -and $record.last_success_utc -and ($now - [DateTimeOffset]::Parse($record.last_success_utc)).TotalSeconds -gt $config.max_success_age_seconds) { $report.state = 'stale' }
    }
    $exitCode = $(if ($report.state -in @('healthy','not_due','running')) { 0 } else { 1 })
} catch { $report.diagnostic = $_.Exception.Message }
finally {
    if ($null -ne $lock) { $lock.Dispose() }
    if ($null -ne $release) { foreach ($handle in $release.Handles) { $handle.Dispose() } }
}
if ($NotifyScript -and $exitCode -ne 0) {
    try {
        $notify = Assert-LocalPath $NotifyScript File
        $notificationPath = Join-Path ([IO.Path]::GetTempPath()) ('rukbat-alert-' + [guid]::NewGuid().ToString('N') + '.json')
        try {
            Write-AtomicJson $notificationPath $report
            Import-ProcessJob
            $hostPath = (Get-Process -Id $PID).Path
            $result = [RukbatProcessJob]::Run($hostPath, (Get-EncodedInvocation $notify @($notificationPath)), $PSScriptRoot, 30000)
            if ($result[0] -ne 0) { throw "Notification failed with exit code $($result[0])." }
        } finally { if ($notificationPath -and [IO.File]::Exists($notificationPath)) { [IO.File]::Delete($notificationPath) } }
    } catch { $report.diagnostic = 'Notification failed: ' + $_.Exception.Message; $exitCode = 2 }
}
ConvertTo-Json -InputObject $report -Depth 5 -Compress
exit $exitCode
