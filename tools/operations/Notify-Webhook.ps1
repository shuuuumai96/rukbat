param([Parameter(Mandatory=$true)][string]$StatusPath)
$ErrorActionPreference = 'Stop'
try {
    # Configure this in the monitor account's environment; do not put credentials in logs or job arguments.
    $endpoint = [Environment]::GetEnvironmentVariable('RUKBAT_ALERT_URL')
    if (-not $endpoint -or ([uri]$endpoint).Scheme -ne 'https') { throw 'RUKBAT_ALERT_URL must be an HTTPS endpoint accepting a JSON POST.' }
    $body = [IO.File]::ReadAllText($StatusPath, [Text.Encoding]::UTF8)
    Invoke-RestMethod -Uri $endpoint -Method Post -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 10 | Out-Null
} catch { [Console]::Error.WriteLine('Alert delivery failed. Check the monitor account and endpoint.'); exit 1 }
