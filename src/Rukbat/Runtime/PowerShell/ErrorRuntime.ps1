$__rukbat_error = {
    param([System.Management.Automation.ErrorRecord]$record, [string]$location)
    $exception = $record.Exception
    $kind = 'other'
    while ($null -ne $exception) {
        if ($exception.Data.Contains('Rukbat.Location')) {
            return @{ message = $exception.Message; __rukbat_location = $exception.Data['Rukbat.Location']; __rukbat_error_kind = $exception.Data['Rukbat.Kind'] }
        }
        if ($exception -is [IO.FileNotFoundException] -or $exception -is [IO.DirectoryNotFoundException]) { $kind = 'not_found' }
        if ($exception -is [UnauthorizedAccessException]) { $kind = 'access_denied' }
        if ($exception -is [IO.IOException]) {
            $sharingViolation = 32
            $lockViolation = 33
            if (($exception.HResult -band 0xffff) -in @($sharingViolation, $lockViolation)) { $kind = 'in_use' }
        }
        $exception = $exception.InnerException
    }
    return @{ message = $record.Exception.Message; __rukbat_location = $location; __rukbat_error_kind = $kind }
}
$__rukbat_raise = {
    param([hashtable]$value, [string]$location)
    $exception = [System.Exception]::new($value.message)
    if ($value.ContainsKey('__rukbat_location')) {
        $location = $value.__rukbat_location
    }
    $exception.Data['Rukbat.Location'] = $location
    $exception.Data['Rukbat.Kind'] = $value['__rukbat_error_kind']
    throw $exception
}
