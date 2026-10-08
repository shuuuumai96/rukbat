'cli.command' {
    return @{ name = $values[0]; description = $values[1]; arguments = @(); options = @(); flags = @() }
}
'cli.argument' {
    return @{ name = $values[0]; description = $values[1]; required = $true; repeatable = $false; allow_empty = $false; choices = @() }
}
'cli.option' {
    return @{ name = $values[0]; description = $values[1]; short_name = $null; value_name = $values[0].ToUpperInvariant();
        required = $false; default_value = $null; repeatable = $false; allow_empty = $false; choices = @() }
}
'cli.flag' { return @{ name = $values[0]; description = $values[1]; short_name = $null } }
'cli.parse' { return ,(& $__rukbat_cli_parse $values[0] $values[1]) }
'cli.help' {
    $null = & $__rukbat_cli_validate $values[0]
    return (& $__rukbat_cli_help $values[0])
}
'cli.help_requested' { return $values[0]['help'] }
{ $_ -cin @('cli.has', 'cli.get', 'cli.get_all') } {
    $result = $values[0]
    if ($result['help']) { throw 'Help was requested. Handle it before reading arguments.' }
    $name = $values[1]
    if (!$result['entries'].ContainsKey($name)) { throw "Unknown CLI item '$name'." }
    $entry = $result['entries'][$name]
    if ($operation -ceq 'cli.has') { return $entry.values.Count -gt 0 }
    if ($entry.kind -ceq 'flag') { throw "Use cli.has to read flag '$name'." }
    if ($operation -ceq 'cli.get_all') {
        if (!$entry.repeatable) { throw "Item '$name' is not repeatable. Use cli.get." }
        return ,([object[]]$entry.values.ToArray())
    }
    if ($entry.repeatable) { throw "Item '$name' is repeatable. Use cli.get_all." }
    if ($entry.values.Count -eq 0) { throw "Item '$name' was not provided and has no default." }
    return [string]$entry.values[0]
}
