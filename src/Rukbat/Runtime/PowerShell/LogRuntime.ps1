{ $_ -cin @('log.enabled', 'log.debug', 'log.info', 'log.warn', 'log.error') } {
    $logger = [object[]]$values[0]
    $level = if ($operation -ceq 'log.enabled') { [string]$values[1] } else { $__rukbat_log_operations[$operation] }
    $outputs = & $__rukbat_log_outputs $logger $level
    if ($operation -ceq 'log.enabled') { return [bool]($outputs[0] -or $outputs[1]) }
    if (-not ($outputs[0] -or $outputs[1])) { return }

    & $__rukbat_load_Log
    $prepared = [RukbatLog]::Prepare([object[]]@($__rukbat_log_levels[$level].label; $values[1]; $outputs[1]))
    $failures = [System.Collections.Generic.List[string]]::new()
    if ($outputs[0]) {
        try { [RukbatLog]::WriteStderr($prepared) }
        catch { $failures.Add('stderr: ' + (& $__rukbat_log_failure $_)) }
    }
    if ($outputs[1]) {
        $path = [string]$logger[1]
        try {
            $path = & $__rukbat_resolve_path $path
            [RukbatLog]::AppendFile([object[]]@($path; (,$prepared)))
        } catch { $failures.Add("file '$path': " + (& $__rukbat_log_failure $_)) }
    }
    if ($failures.Count -ne 0) {
        throw ($operation + ': ' + [string]::Join('; ', $failures.ToArray()))
    }
    return
}
