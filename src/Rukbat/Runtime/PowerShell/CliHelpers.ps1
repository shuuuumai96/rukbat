$__rukbat_cli_value_error = {
    param($item, [string]$value, [string]$label)
    if ($value.Length -eq 0 -and !$item.allow_empty) { return "$label requires a non-empty value." }
    if ($item.choices.Length -ne 0) {
        $choiceMatched = $false
        foreach ($choice in $item.choices) {
            if ([string]::Equals($choice, $value, [StringComparison]::Ordinal)) { $choiceMatched = $true; break }
        }
        if (!$choiceMatched) { return "$label must be one of: " + [string]::Join(', ', [string[]]$item.choices) + '.' }
    }
    return $null
}
$__rukbat_cli_validate = {
    param($command)
    if ([string]::IsNullOrWhiteSpace($command.name) -or $command.name -cmatch '[\x00-\x1f\x7f]') {
        throw 'Invalid CLI definition: command name must be non-empty and contain no control characters.'
    }
    $names = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $spellings = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $optionalArgument = $false
    foreach ($kind in @('argument', 'option', 'flag')) {
        $items = switch ($kind) { 'argument' { ,$command.arguments }; 'option' { ,$command.options }; 'flag' { ,$command.flags } }
        for ($index = 0; $index -lt $items.Length; $index++) {
            $item = $items[$index]
            $name = $item.name
            if ($name -cnotmatch '\A[a-z][a-z0-9]*(?:-[a-z0-9]+)*\z' -or $name -ceq 'help') {
                throw "Invalid CLI definition: invalid or reserved name '$name'."
            }
            if ($names.ContainsKey($name)) { throw "Invalid CLI definition: duplicate name '$name'." }
            $names.Add($name, @{ kind = $kind; item = $item })
            if ($kind -cne 'argument') {
                $spellings.Add('--' + $name, $names[$name])
                if ($null -ne $item.short_name) {
                    $short = $item.short_name
                    if ($short -cnotmatch '\A[a-z]\z' -or $short -ceq 'h') { throw "Invalid CLI definition: invalid or reserved short name '$short'." }
                    if ($spellings.ContainsKey('-' + $short)) { throw "Invalid CLI definition: duplicate short name '-$short'." }
                    $spellings.Add('-' + $short, $names[$name])
                }
            }
            if ($kind -ceq 'flag') { continue }
            $choices = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($choice in $item.choices) {
                if (!$choices.Add($choice)) { throw "Invalid CLI definition: duplicate choice '$choice' for '$name'." }
                if ($choice.Length -eq 0 -and !$item.allow_empty) { throw "Invalid CLI definition: '$name' does not allow an empty choice." }
            }
            if ($kind -ceq 'argument') {
                if ($item.required -and $optionalArgument) { throw "Invalid CLI definition: required argument '$name' follows an optional argument." }
                if (!$item.required) { $optionalArgument = $true }
                if ($item.repeatable -and $index -ne $items.Length - 1) { throw "Invalid CLI definition: repeatable argument '$name' must be last." }
            } else {
                if ([string]::IsNullOrWhiteSpace($item.value_name) -or $item.value_name -cmatch '\s|[\x00-\x1f\x7f]') {
                    throw "Invalid CLI definition: '$name' needs a single non-empty value name."
                }
                if ($null -ne $item.default_value) {
                    if ($item.required -or $item.repeatable) { throw "Invalid CLI definition: '$name' cannot combine a default with required or repeatable." }
                    $errorText = & $__rukbat_cli_value_error $item $item.default_value "Default for '$name'"
                    if ($null -ne $errorText) { throw ('Invalid CLI definition: ' + $errorText) }
                }
            }
        }
    }
    return @{ names = $names; spellings = $spellings }
}.GetNewClosure()
$__rukbat_cli_usage = {
    param($command)
    $parts = [System.Collections.Generic.List[string]]::new()
    $parts.Add('Usage: ' + $command.name)
    $parts.Add('[options]')
    foreach ($option in $command.options) {
        if ($option.required) { $parts.Add('--' + $option.name + ' ' + $option.value_name) }
    }
    foreach ($argument in $command.arguments) {
        $label = $argument.name.ToUpperInvariant()
        if ($argument.repeatable) { $label += '...' }
        if (!$argument.required) { $label = '[' + $label + ']' }
        $parts.Add($label)
    }
    return [string]::Join(' ', $parts.ToArray())
}
$__rukbat_cli_suggestion = {
    param([string]$spelling, $spellings)
    $candidate = $null
    foreach ($name in $spellings.Keys) {
        if ($name.StartsWith('--', [StringComparison]::Ordinal) -ne $spelling.StartsWith('--', [StringComparison]::Ordinal)) { continue }
        if ([Math]::Abs($name.Length - $spelling.Length) -gt 1) { continue }
        $left = 0; $right = 0; $differences = 0
        while ($left -lt $name.Length -and $right -lt $spelling.Length) {
            if ($name[$left] -ceq $spelling[$right]) { $left++; $right++; continue }
            $differences++
            if ($differences -gt 1) { break }
            if ($name.Length -ge $spelling.Length) { $left++ }
            if ($name.Length -le $spelling.Length) { $right++ }
        }
        $differences += ($name.Length - $left) + ($spelling.Length - $right)
        if ($differences -ne 1) { continue }
        if ($null -ne $candidate) { return $null }
        $candidate = $name
    }
    return $candidate
}
$__rukbat_cli_help = {
    param($command)
    $lines = [System.Collections.Generic.List[string]]::new()
    if ($command.description.Length -ne 0) { $lines.Add($command.description.Replace("`r`n", "`n").Replace("`r", "`n")); $lines.Add('') }
    $lines.Add((& $__rukbat_cli_usage $command))
    if ($command.arguments.Length -ne 0) {
        $lines.Add(''); $lines.Add('Arguments:')
        foreach ($item in $command.arguments) {
            $label = $item.name.ToUpperInvariant()
            if ($item.repeatable) { $label += '...' }
            if (!$item.required) { $label = '[' + $label + ']' }
            $lines.Add('  ' + $label)
            foreach ($line in $item.description.Replace("`r`n", "`n").Replace("`r", "`n").Split([char]10)) { $lines.Add('    ' + $line) }
            if ($item.choices.Length -ne 0) { $lines.Add('    Choices: ' + [string]::Join(', ', [string[]]$item.choices)) }
            $lines.Add('')
        }
        $lines.RemoveAt($lines.Count - 1)
    }
    $lines.Add(''); $lines.Add('Options:')
    foreach ($kind in @('option', 'flag')) {
        $items = if ($kind -ceq 'option') { ,$command.options } else { ,$command.flags }
        foreach ($item in $items) {
            $label = '--' + $item.name
            if ($null -ne $item.short_name) { $label = '-' + $item.short_name + ', ' + $label }
            if ($kind -ceq 'option') {
                $label += ' ' + $item.value_name
                if ($item.repeatable) { $label += ' (repeatable)' }
                if ($item.required) { $label += ' (required)' }
            }
            $lines.Add('  ' + $label)
            foreach ($line in $item.description.Replace("`r`n", "`n").Replace("`r", "`n").Split([char]10)) { $lines.Add('    ' + $line) }
            if ($kind -ceq 'option') {
                if ($null -ne $item.default_value) { $lines.Add('    Default: "' + $item.default_value.Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t') + '"') }
                if ($item.choices.Length -ne 0) { $lines.Add('    Choices: ' + [string]::Join(', ', [string[]]$item.choices)) }
            }
            $lines.Add('')
        }
    }
    $lines.Add('  -h, --help'); $lines.Add('    Show this help.')
    return [string]::Join("`n", $lines.ToArray())
}.GetNewClosure()
$__rukbat_cli_parse = {
    param($command, [object[]]$arguments)
    $definition = & $__rukbat_cli_validate $command
    $entries = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($name in $definition.names.Keys) {
        $bound = $definition.names[$name]
        $entries.Add($name, @{ kind = $bound.kind; repeatable = ($bound.kind -cne 'flag' -and $bound.item.repeatable);
            values = [System.Collections.Generic.List[string]]::new() })
    }
    $errors = [System.Collections.Generic.List[object]]::new()
    $positionals = [System.Collections.Generic.List[object]]::new()
    $help = $false
    $optionsEnded = $false
    for ($index = 0; $index -lt $arguments.Length; $index++) {
        $tokenPosition = $index
        $token = [string]$arguments[$index]
        if ($optionsEnded) { $positionals.Add(@{ value = $token; position = $index }); continue }
        if ($token -ceq '--') { $optionsEnded = $true; continue }
        if ($token -ceq '--help' -or $token -ceq '-h') { $help = $true; continue }
        if (!$token.StartsWith('-', [StringComparison]::Ordinal) -or $token -ceq '-' -or $token -cmatch '\A-[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z') {
            $positionals.Add(@{ value = $token; position = $index }); continue
        }
        $equals = $token.IndexOf('=')
        $spelling = if ($equals -lt 0) { $token } else { $token.Substring(0, $equals) }
        if (!$definition.spellings.ContainsKey($spelling)) {
            $message = "Unknown option '$spelling'."
            $suggestion = & $__rukbat_cli_suggestion $spelling $definition.spellings
            if ($null -ne $suggestion) { $message += " Did you mean '$suggestion'?" }
            $errors.Add(@{ position = $index; message = $message }); continue
        }
        $bound = $definition.spellings[$spelling]
        $item = $bound.item
        $entry = $entries[$item.name]
        if ($entry.values.Count -ne 0 -and !$entry.repeatable) { $errors.Add(@{ position = $index; message = "Option '--$($item.name)' may only be specified once." }) }
        if ($bound.kind -ceq 'flag') {
            if ($equals -ge 0) { $errors.Add(@{ position = $index; message = "Flag '--$($item.name)' does not take a value." }) }
            $entry.values.Add('true'); continue
        }
        $value = $null
        if ($equals -ge 0) { $value = $token.Substring($equals + 1) }
        elseif ($index + 1 -lt $arguments.Length) {
            $next = [string]$arguments[$index + 1]
            if (!$next.StartsWith('-', [StringComparison]::Ordinal) -or $next -ceq '-' -or $next -cmatch '\A-[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z') {
                $index++; $value = $next
            }
        }
        if ($null -eq $value) {
            $errors.Add(@{ position = $tokenPosition; message = "Option '--$($item.name)' requires a value. Use --$($item.name)=VALUE when the value begins with '-'." }); continue
        }
        $entry.values.Add($value)
        $errorText = & $__rukbat_cli_value_error $item $value "Option '--$($item.name)'"
        if ($null -ne $errorText) { $errors.Add(@{ position = $tokenPosition; message = $errorText }) }
    }
    if (!$help) {
        $position = 0
        foreach ($item in $command.arguments) {
            $entry = $entries[$item.name]
            $limit = if ($item.repeatable) { $positionals.Count } else { [Math]::Min($position + 1, $positionals.Count) }
            while ($position -lt $limit) {
                $supplied = $positionals[$position]; $position++
                $entry.values.Add($supplied.value)
                $errorText = & $__rukbat_cli_value_error $item $supplied.value "Argument '$($item.name)'"
                if ($null -ne $errorText) { $errors.Add(@{ position = $supplied.position; message = $errorText }) }
            }
            if ($item.required -and $entry.values.Count -eq 0) { $errors.Add(@{ position = $arguments.Length; message = "Missing required argument '$($item.name)'." }) }
        }
        if ($position -lt $positionals.Count) { $errors.Add(@{ position = $positionals[$position].position; message = 'Too many positional arguments.' }) }
        foreach ($item in $command.options) {
            $entry = $entries[$item.name]
            if ($entry.values.Count -eq 0) {
                if ($item.required) { $errors.Add(@{ position = $arguments.Length; message = "Missing required option '--$($item.name)'." }) }
                elseif ($null -ne $item.default_value) { $entry.values.Add($item.default_value) }
            }
        }
        if ($errors.Count -ne 0) {
            $first = $errors[0]
            foreach ($failure in $errors) { if ($failure.position -lt $first.position) { $first = $failure } }
            throw ($first.message + "`n" + (& $__rukbat_cli_usage $command) + "`nUse --help for details.")
        }
    }
    $result = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $result.Add('help', $help); $result.Add('entries', $entries)
    return ,$result
}.GetNewClosure()
