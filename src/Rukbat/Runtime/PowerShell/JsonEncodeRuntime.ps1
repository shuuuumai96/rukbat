$__rukbat_json_encode = {
    param([object]$value, [hashtable]$schema)
    & $__rukbat_load_Json
    function __rukbat_json_quote {
        param([string]$text)
        return [RukbatJsonValue]::Quote($text)
    }
    function __rukbat_json_encode_value {
        param([object]$item, [hashtable]$type)
        if ($null -eq $item) {
            if ($type.kind -eq 'nullable') { return 'null' }
            throw "Null is incompatible with $($type.name)."
        }
        if ($type.kind -eq 'nullable') { return (__rukbat_json_encode_value $item $type.element) }
        switch ($type.kind) {
            'json' { return $item.ToJson() }
            'string' { return (__rukbat_json_quote ([string]$item)) }
            'enum' { return (__rukbat_json_quote ([string]$item)) }
            'bool' { return $(if ($item) { 'true' } else { 'false' }) }
            'int' { return ([long]$item).ToString([System.Globalization.CultureInfo]::InvariantCulture) }
            'float' {
                $number = [double]$item
                if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) {
                    throw 'Cannot encode a non-finite float as JSON.'
                }
                return $number.ToString('R', [System.Globalization.CultureInfo]::InvariantCulture)
            }
            'array' {
                $parts = [System.Collections.Generic.List[string]]::new()
                foreach ($element in $item) { $null = $parts.Add((__rukbat_json_encode_value $element $type.element)) }
                return '[' + [string]::Join(',', $parts) + ']'
            }
            'dictionary' {
                $parts = [System.Collections.Generic.List[string]]::new()
                $keys = [string[]]@($item['entries'].Keys)
                [Array]::Sort($keys, [StringComparer]::Ordinal)
                foreach ($key in $keys) {
                    $parts.Add((__rukbat_json_quote $key) + ':' + (__rukbat_json_encode_value $item['entries'][$key]['value'] $type.element))
                }
                return '{' + [string]::Join(',', $parts) + '}'
            }
            'struct' {
                $parts = [System.Collections.Generic.List[string]]::new()
                foreach ($field in $type.fields) {
                    $parts.Add((__rukbat_json_quote $field.name) + ':' + (__rukbat_json_encode_value $item[$field.key] $field.type)) > $null
                }
                return '{' + [string]::Join(',', $parts) + '}'
            }
        }
        throw "Type $($type.name) cannot be encoded as JSON."
    }
    return (__rukbat_json_encode_value $value $schema)
}.GetNewClosure()
