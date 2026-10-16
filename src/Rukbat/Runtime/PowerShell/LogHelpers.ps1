$__rukbat_log_outputs = {
    param([object[]]$logger, [string]$level)
    $rank = $__rukbat_log_levels[$level].rank
    $stderr = $null -ne $logger[0] -and $rank -ge $__rukbat_log_levels[$logger[0]].rank
    $file = $null -ne $logger[1] -and $rank -ge $__rukbat_log_levels[$logger[2]].rank
    return ,([object[]]@($stderr, $file))
}.GetNewClosure()
$__rukbat_log_failure = {
    param([System.Management.Automation.ErrorRecord]$record)
    $exception = $record.Exception
    # Unwrap the host invocation wrapper, but retain native cleanup diagnostics.
    while ($null -ne $exception.InnerException -and
        ($exception -is [System.Management.Automation.MethodInvocationException] -or
            $exception -is [System.Reflection.TargetInvocationException])) {
        $exception = $exception.InnerException
    }
    return $exception.Message
}.GetNewClosure()
