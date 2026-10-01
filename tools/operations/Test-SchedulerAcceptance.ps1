[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ConfigPath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$registered = $false
$outcome = [ordered]@{ schema_version = 1; checked_utc = [DateTimeOffset]::UtcNow.ToString('o'); registration = 'unverified'; scheduled_execution = 'unverified'; cleanup = 'not_needed'; diagnostic = $null }
$exitCode = 2
try {
    Assert-OperationsHost
    $config = Read-JobConfig $ConfigPath
    if ($config.job_id -notmatch '^scheduler-acceptance-[a-f0-9]+$') { throw 'Use a disposable scheduler-acceptance-<hex> job, never a production job.' }
    $taskName = 'Rukbat-' + $config.job_id
    $taskPath = '\'
    $manager = Join-Path $config.release_directory 'Manage-Task.ps1'
    # Use a separate host so the tool exit code is unambiguous.
    & $config.powershell -NoLogo -NoProfile -NonInteractive -File $manager -Action Install -ConfigPath $ConfigPath -At 23:59
    if ($LASTEXITCODE -ne 0) { throw "Task registration failed with exit code $LASTEXITCODE. Check delegated scheduler rights or run this acceptance test from an authorized administrator session." }
    $registered = $true
    $outcome.registration = 'passed'
    $statePath = Get-StatePath $config
    $priorId = $(if ([IO.File]::Exists($statePath)) { (Read-RunRecord $config).run_id } else { $null })
    Start-ScheduledTask -TaskName $taskName -TaskPath $taskPath
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 250
        if ([IO.File]::Exists($statePath)) {
            $record = Read-RunRecord $config
            if ($record.run_id -cne $priorId -and $record.status -ne 'running' -and (Get-ScheduledTask -TaskName $taskName -TaskPath $taskPath).State -ne 'Running') {
                if ($record.status -ne 'succeeded') { throw "Scheduled job status: $($record.status)" }
                if ((Get-ScheduledTaskInfo -TaskName $taskName -TaskPath $taskPath).LastTaskResult -ne 0) { throw 'The scheduler did not return zero.' }
                if ([IO.File]::ReadAllText((Join-Path $config.working_directory 'scheduled.txt')) -cne 'completed') { throw 'The scheduled business output did not match.' }
                $outcome.scheduled_execution = 'passed'
                break
            }
        }
    } while ($timer.Elapsed.TotalSeconds -lt ($config.timeout_seconds + 60))
    if ($outcome.scheduled_execution -ne 'passed') { throw 'The scheduler did not produce a successful run record in the startup window.' }
    $outcome.run_id = $record.run_id
    $outcome.task_information = Get-ScheduledTaskInfo -TaskName $taskName -TaskPath $taskPath | Select-Object LastRunTime,LastTaskResult,NextRunTime
    $exitCode = 0
} catch { $outcome.diagnostic = $_.Exception.Message; [Console]::Error.WriteLine($outcome.diagnostic) }
finally {
    if ($registered) {
        try {
            if ((Get-ScheduledTask -TaskName $taskName -TaskPath $taskPath).State -eq 'Running') {
                Stop-ScheduledTask -TaskName $taskName -TaskPath $taskPath
            }
            # Only the task registered by this invocation is removed. Fixture data and logs remain.
            Unregister-ScheduledTask -TaskName $taskName -TaskPath $taskPath -Confirm:$false
            $outcome.cleanup = 'passed'
        } catch { $outcome.cleanup = 'failed'; $outcome.diagnostic = $_.Exception.Message; $exitCode = 2 }
    }
}
ConvertTo-Json -InputObject $outcome -Depth 6
exit $exitCode
