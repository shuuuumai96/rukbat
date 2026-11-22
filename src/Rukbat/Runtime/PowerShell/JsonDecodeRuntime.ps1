$__rukbat_json_decode = {
    param([object]$text, [hashtable]$schema, [bool]$fromValue, [scriptblock]$dictionaryRuntime, [bool]$strict)
    & $__rukbat_load_Json
    if ($fromValue) { $node = $text.Node }
    else { $node = [RukbatJsonParser]::Parse([string]$text) }
    function __rukbat_json_convert {
        param([object]$value, [hashtable]$type, [string]$path)
        if ($null -eq $value) {
            if ($type.kind -eq 'nullable') { return $null }
            if ($type.kind -eq 'json') { return [RukbatJsonValue]::new($null) }
            throw "${path}: JSON null is incompatible with $($type.name)."
        }
        if ($type.kind -eq 'nullable') {
            return ,(__rukbat_json_convert $value $type.element $path)
        }
        switch ($type.kind) {
            'json' { return [RukbatJsonValue]::new($value) }
            'string' {
                if ($value -is [string]) { return $value }
            }
            'enum' {
                if ($value -is [string] -and [Array]::IndexOf($type.members, $value) -ge 0) {
                    return $value
                }
            }
            'bool' {
                if ($value -is [bool]) { return $value }
            }
            'int' {
                if ($value -is [RukbatJsonNumber] -and $value.Text -notmatch '[.eE]') {
                    $result = [long]0
                    if ([long]::TryParse($value.Text,
                        [System.Globalization.NumberStyles]::AllowLeadingSign,
                        [System.Globalization.CultureInfo]::InvariantCulture, [ref]$result)) {
                        return $result
                    }
                }
            }
            'float' {
                if ($value -is [RukbatJsonNumber]) {
                    $result = [double]0
                    if ([double]::TryParse($value.Text,
                        [System.Globalization.NumberStyles]::Float,
                        [System.Globalization.CultureInfo]::InvariantCulture, [ref]$result) -and
                        -not [double]::IsNaN($result) -and -not [double]::IsInfinity($result) -and
                        ($result -ne 0 -or (($value.Text -split '[eE]')[0] -cnotmatch '[1-9]'))) {
                        return $result
                    }
                }
            }
            'array' {
                if ($value -is [array]) {
                    $copy = [System.Collections.Generic.List[object]]::new($value.Length)
                    for ($i = 0; $i -lt $value.Length; $i++) {
                        $copy.Add((__rukbat_json_convert $value[$i] $type.element "$path[$i]")) > $null
                    }
                    return ,$copy
                }
            }
            'dictionary' {
                if ($value -is [System.Collections.Generic.Dictionary[string,object]]) {
                    $copy = & $dictionaryRuntime 'new' $null 'string'
                    $keys = [string[]]@($value.Keys)
                    [Array]::Sort($keys, [StringComparer]::Ordinal)
                    foreach ($key in $keys) {
                        $converted = __rukbat_json_convert $value[$key] $type.element "$path[$key]"
                        & $dictionaryRuntime 'set' $copy $key $converted
                    }
                    return $copy
                }
            }
            'struct' {
                if ($value -is [System.Collections.Generic.Dictionary[string,object]]) {
                    $copy = @{}
                    foreach ($field in $type.fields) {
                        if (-not $value.ContainsKey($field.name)) {
                            throw "$path.$($field.name): Missing required property."
                        }
                        $copy[$field.key] = __rukbat_json_convert $value[$field.name] $field.type "$path.$($field.name)"
                    }
                    if ($strict -and $value.Count -ne $type.fields.Count) {
                        $expected = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                        foreach ($field in $type.fields) { $null = $expected.Add($field.name) }
                        foreach ($name in $value.Keys) {
                            if (-not $expected.Contains($name)) { throw "$path.$($name): Unknown property." }
                        }
                    }
                    return $copy
                }
            }
        }
        throw "${path}: JSON value is incompatible with $($type.name)."
    }
    # Forward one value without enumerating empty or singleton arrays.
    return ,(__rukbat_json_convert $node $schema '$')
}.GetNewClosure()
