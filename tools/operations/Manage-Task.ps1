[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Export','Install','Inspect','Remove')][string]$Action,
    [Parameter(Mandatory=$true)][string]$ConfigPath,
    [ValidatePattern('^([01][0-9]|2[0-3]):[0-5][0-9]$')][string]$At = '01:00',
    [string]$Account = [Security.Principal.WindowsIdentity]::GetCurrent().Name,
    [string]$XmlPath
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$release = $null
try {
    Assert-OperationsHost
    $ConfigPath = Assert-LocalPath $ConfigPath File
    $config = Read-JobConfig $ConfigPath
    $release = Open-Release $config
    Import-Module ScheduledTasks -ErrorAction Stop
    $taskName = 'Rukbat-' + $config.job_id
    $taskPath = '\'
    $description = 'Rukbat job configuration: ' + $ConfigPath
    if ($Action -in @('Inspect','Remove')) {
        $existing = Get-ScheduledTask -TaskName $taskName -TaskPath $taskPath -ErrorAction Stop
        if ($existing.Description -cne $description) { throw 'Task ownership does not match this configuration.' }
        if ($Action -eq 'Remove') {
            if ($existing.State -eq 'Running') { throw 'Stop and reconcile the running job before removing its task.' }
            Unregister-ScheduledTask -TaskName $taskName -TaskPath $taskPath -Confirm:$false
        } else {
            $info = Get-ScheduledTaskInfo -TaskName $taskName -TaskPath $taskPath
            [pscustomobject]@{ task = $existing; information = $info } | ConvertTo-Json -Depth 8
        }
    } else {
        $scriptPath = Join-Path $PSScriptRoot 'Invoke-Job.ps1'
        $command = "& '" + $scriptPath.Replace("'", "''") + "' -ConfigPath '" + $ConfigPath.Replace("'", "''") + "'"
        $arguments = '-NoLogo -NoProfile -NonInteractive -EncodedCommand ' + [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
        $taskAction = New-ScheduledTaskAction -Execute $config.powershell -Argument $arguments -WorkingDirectory $config.working_directory
        $trigger = New-ScheduledTaskTrigger -Daily -At ([datetime]::ParseExact($At, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture))
        # The runner owns retries and its shorter deadline; this is a final scheduler backstop.
        $settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Seconds ($config.timeout_seconds + 60)) -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
        $principal = New-ScheduledTaskPrincipal -UserId $Account -LogonType S4U -RunLevel Limited
        $task = New-ScheduledTask -Action $taskAction -Trigger $trigger -Settings $settings -Principal $principal -Description $description
        if ($Action -eq 'Export') {
            if (-not $XmlPath) { throw 'Export requires XmlPath.' }
            $XmlPath = Assert-LocalPath $XmlPath New
            $taskXml = $task | Export-ScheduledTask
            [IO.File]::WriteAllText($XmlPath, $taskXml, [Text.Encoding]::Unicode)
        } else {
            # No -Force: never overwrite a pre-existing task, including one with the same job ID.
            Register-ScheduledTask -TaskName $taskName -TaskPath $taskPath -InputObject $task -ErrorAction Stop | Out-Null
            Get-ScheduledTaskInfo -TaskName $taskName -TaskPath $taskPath | ConvertTo-Json
        }
    }
} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 2 }
finally { if ($release) { foreach ($handle in $release.Handles) { $handle.Dispose() } } }
