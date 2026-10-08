        $__rukbat_json_decode = {
            param([object]$text, [hashtable]$schema, [bool]$fromValue, [scriptblock]$dictionaryRuntime)
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
                    return (__rukbat_json_convert $value $type.element $path)
                }
                switch ($type.kind) {
                    'json' { return [RukbatJsonValue]::new($value) }
                    'string' {
                        if ($value -is [string]) { return $value }
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
                            return $copy
                        }
                    }
                }
                throw "${path}: JSON value is incompatible with $($type.name)."
            }
            return (__rukbat_json_convert $node $schema '$')
        }
        $__rukbat_json_encode = {
            param([object]$value, [hashtable]$schema)
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
        }
