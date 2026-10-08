$__rukbat_dictionary = {
    param([string]$operation, [object]$dictionary, [object]$key, [object]$value, [string]$location, [scriptblock]$raise)
    if ($operation -eq 'new') {
        $entries = if ($key -eq 'string') {
            [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        } else { [System.Collections.Generic.Dictionary[long,object]]::new() }
        return @{ __rukbat_dictionary = $true; entries = $entries; order = [System.Collections.Generic.LinkedList[object]]::new() }
    }
    $entries = $dictionary['entries']
    if ($operation -eq 'keys') {
        $keys = [System.Collections.Generic.List[object]]::new($entries.Count)
        foreach ($storedKey in $dictionary['order']) { $keys.Add($storedKey) }
        return ,$keys
    }
    $found = $entries.ContainsKey($key)
    if ($operation -eq 'has') { return $found }
    if ($operation -eq 'remove') {
        if ($found) {
            $dictionary['order'].Remove($entries[$key]['node'])
            $null = $entries.Remove($key)
        }
        return
    }
    if ($operation -eq 'add' -and $found) {
        & $raise @{ message = "Duplicate dictionary key '$key'." } $location
    }
    if ($operation -eq 'set' -or $operation -eq 'add') {
        if ($found) { $entries[$key]['value'] = $value }
        else { $entries.Add($key, @{ value = $value; node = $dictionary['order'].AddLast([object]$key) }) }
        return
    }
    if (-not $found) {
        & $raise @{ message = "Dictionary has no key '$key'." } $location
    }
    if ($operation -eq 'entry') { return $entries[$key] }
    return ,$entries[$key]['value']
}
$__rukbat_array = {
    param([object[]]$values)
    $array = [System.Collections.Generic.List[object]]::new($values.Length)
    foreach ($value in $values) {
        $array.Add($value) > $null
    }
    return ,$array
}
$__rukbat_clone = {
    param([object]$value, [scriptblock]$recurse)
    if ($value -is [System.Collections.Generic.List[object]]) {
        $copy = [System.Collections.Generic.List[object]]::new($value.Count)
        for ($i = 0; $i -lt $value.Count; $i++) {
            $copy.Add((& $recurse $value[$i] $recurse)) > $null
        }
        return ,$copy
    }
    if ($value -is [hashtable]) {
        if ($value.ContainsKey('__rukbat_dictionary')) {
            $entries = if ($value['entries'] -is [System.Collections.Generic.Dictionary[string,object]]) {
                [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
            } else { [System.Collections.Generic.Dictionary[long,object]]::new() }
            $order = [System.Collections.Generic.LinkedList[object]]::new()
            foreach ($key in $value['order']) {
                $entries.Add($key, @{ value = (& $recurse $value['entries'][$key]['value'] $recurse); node = $order.AddLast([object]$key) })
            }
            return @{ __rukbat_dictionary = $true; entries = $entries; order = $order }
        }
        $copy = @{}
        foreach ($key in $value.Keys) {
            $copy[$key] = & $recurse $value[$key] $recurse
        }
        return $copy
    }
    return $value
}
$__rukbat_append = {
    param([System.Collections.Generic.List[object]]$values, [object]$value)
    $values.Add($value) > $null
    return ,$values
}
$__rukbat_index = {
    param([System.Collections.Generic.List[object]]$values, [long]$offset, [string]$location, [bool]$address, [scriptblock]$raise)
    if ($offset -lt 0 -or $offset -ge $values.Count) {
        & $raise @{ message = "Array index $offset is outside length $($values.Count)." } $location
    }
    if ($address) {
        return @{ array = $values; offset = $offset }
    }
    return ,$values[$offset]
}
$__rukbat_float = {
    param([double]$value, [string]$location, [scriptblock]$raise)
    if ([double]::IsNaN($value) -or [double]::IsInfinity($value)) {
        & $raise @{ message = 'Float operation produced a non-finite value.' } $location
    }
    return $value
}
$__rukbat_runtime = {
    param([hashtable]$schema, [string]$command, [object[]]$positional, [hashtable]$named, [string]$location,
        [scriptblock]$normalize, [scriptblock]$raise)
    $ErrorActionPreference = 'Stop'
    try {
        $convert = {
            param([object]$value, [hashtable]$type, [string]$path, [scriptblock]$recurse)
            if ($null -eq $value -and $type.kind -eq 'nullable') {
                return $null
            }
            if ($null -eq $value) {
                throw "${path}: Null is incompatible with $($type.name)."
            }
            switch ($type.kind) {
                'nullable' {
                    return (& $recurse $value $type.element $path $recurse)
                }
                'array' {
                    if ($value -isnot [array]) {
                        throw "${path}: Value is incompatible with $($type.name); expected an array."
                    }
                    $copy = [System.Collections.Generic.List[object]]::new($value.Length)
                    for ($i = 0; $i -lt $value.Length; $i++) {
                        $copy.Add((& $recurse $value[$i] $type.element "$path[$i]" $recurse)) > $null
                    }
                    return ,$copy
                }
                'struct' {
                    if ($value -is [string] -or $value -is [ValueType] -or $value -is [array]) {
                        throw "${path}: Value is incompatible with $($type.name); expected an object."
                    }
                    $copy = @{}
                    foreach ($field in $type.fields) {
                        $found = $false
                        $fieldValue = $null
                        if ($value -is [System.Collections.IDictionary]) {
                            foreach ($key in $value.Keys) {
                                if ($key -is [string] -and $key -ceq $field.name) {
                                    $fieldValue = $value[$key]
                                    $found = $true
                                    break
                                }
                            }
                        } else {
                            foreach ($property in $value.PSObject.Properties) {
                                if ($property.Name -ceq $field.name) {
                                    $fieldValue = $property.Value
                                    $found = $true
                                    break
                                }
                            }
                        }
                        $fieldPath = "$path.$($field.name)"
                        if (-not $found) {
                            throw "${fieldPath}: Missing required field."
                        }
                        $copy[$field.key] = & $recurse $fieldValue $field.type $fieldPath $recurse
                    }
                    return $copy
                }
                'int' {
                    $integral = $value -is [sbyte] -or $value -is [byte] -or
                        $value -is [int16] -or $value -is [uint16] -or
                        $value -is [int] -or $value -is [uint32] -or
                        $value -is [long] -or $value -is [ulong]
                    if ($integral -and [decimal]$value -ge [long]::MinValue -and [decimal]$value -le [long]::MaxValue) {
                        return [long]$value
                    }
                }
                'string' {
                    if ($value -is [string]) {
                        # Windows PowerShell 5.1 can attach adapted properties to string output
                        # from commands such as Get-ChildItem -Name. Cast to keep an Rukbat string scalar.
                        return [string]$value
                    }
                }
                'bool' {
                    if ($value -is [bool]) {
                        return $value
                    }
                }
                'float' {
                    $numeric = $value -is [sbyte] -or $value -is [byte] -or
                        $value -is [int16] -or $value -is [uint16] -or
                        $value -is [int] -or $value -is [uint32] -or
                        $value -is [long] -or $value -is [ulong] -or
                        $value -is [single] -or $value -is [double] -or $value -is [decimal]
                    if ($numeric) {
                        $converted = [double]$value
                        if (-not [double]::IsNaN($converted) -and -not [double]::IsInfinity($converted)) {
                            return $converted
                        }
                    }
                }
            }
            throw "${path}: Value is incompatible with $($type.name)."
        }
        $commandInfo = $ExecutionContext.InvokeCommand.GetCommand($command, [System.Management.Automation.CommandTypes]::All)
        while ($commandInfo -is [System.Management.Automation.AliasInfo]) {
            $commandInfo = $commandInfo.ResolvedCommand
        }
        # Rukbat normalizes native failures itself, independently of the host's preference.
        $result = @(& $command @positional @named)
        if ($null -ne $commandInfo -and $commandInfo.CommandType -eq [System.Management.Automation.CommandTypes]::Application) {
            $successExitCode = 0
            if ($LASTEXITCODE -ne $successExitCode) {
                throw "Native command '$command' exited with code $LASTEXITCODE."
            }
        }
        if ($schema.kind -eq 'void') {
            return
        }
        $path = "PowerShell call '$command' result"
        if ($schema.kind -eq 'array') {
            $value = & $convert $result $schema $path $convert
            return ,$value
        }
        if ($schema.kind -eq 'nullable' -and $schema.element.kind -eq 'array') {
            if ($result.Count -eq 0) {
                throw "PowerShell call '$command' expected one $($schema.name) result, got 0."
            }
            if ($result.Count -eq 1 -and $null -eq $result[0]) {
                return $null
            }
            $value = & $convert $result $schema.element $path $convert
            return ,$value
        }
        if ($result.Count -ne 1) {
            throw "PowerShell call '$command' expected one $($schema.name) result, got $($result.Count)."
        }
        return (& $convert $result[0] $schema $path $convert)
    } catch {
        & $raise (& $normalize $_ $location) $location
    }
}
