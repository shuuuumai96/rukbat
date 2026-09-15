using System.Text;

namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitJsonRuntime()
    {
        const string runtime = """
        $__rukbat_json_decode = {
            param([string]$text, [hashtable]$schema)
            $state = @{ index = 0 }
            $fail = {
                param([string]$message)
                throw "JSON at offset $($state.index): $message"
            }
            $skip = {
                while ($state.index -lt $text.Length -and
                    ($text[$state.index] -eq [char]32 -or $text[$state.index] -eq [char]9 -or
                     $text[$state.index] -eq [char]10 -or $text[$state.index] -eq [char]13)) {
                    $state.index++
                }
            }
            $parseString = {
                if ($state.index -ge $text.Length -or $text[$state.index] -ne [char]34) {
                    & $fail 'Expected a string.'
                }
                $state.index++
                $builder = [System.Text.StringBuilder]::new()
                while ($state.index -lt $text.Length) {
                    $character = $text[$state.index]
                    $state.index++
                    if ($character -eq [char]34) {
                        return $builder.ToString()
                    }
                    if ([int]$character -lt 32) {
                        & $fail 'Unescaped control character in string.'
                    }
                    if ($character -ne [char]92) {
                        $null = $builder.Append($character)
                        continue
                    }
                    if ($state.index -ge $text.Length) {
                        & $fail 'Incomplete string escape.'
                    }
                    $escape = $text[$state.index]
                    $state.index++
                    if ($escape -eq 'u') {
                        if ($state.index + 4 -gt $text.Length) {
                            & $fail 'Incomplete Unicode escape.'
                        }
                        $hex = $text.Substring($state.index, 4)
                        if ($hex -notmatch '^[0-9A-Fa-f]{4}$') {
                            & $fail 'Invalid Unicode escape.'
                        }
                        $state.index += 4
                        $null = $builder.Append([char][Convert]::ToInt32($hex, 16))
                        continue
                    }
                    $escaped = switch ([string]$escape) {
                        '"' { [char]34; break }
                        '\' { [char]92; break }
                        '/' { [char]47; break }
                        'b' { [char]8; break }
                        'f' { [char]12; break }
                        'n' { [char]10; break }
                        'r' { [char]13; break }
                        't' { [char]9; break }
                        default { & $fail "Unsupported escape '\$escape'." }
                    }
                    $null = $builder.Append($escaped)
                }
                & $fail 'Unterminated string.'
            }
            $parseValue = $null
            $parseValue = {
                & $skip
                if ($state.index -ge $text.Length) {
                    & $fail 'Expected a value.'
                }
                $character = $text[$state.index]
                if ($character -eq [char]34) {
                    return (& $parseString)
                }
                if ($character -eq '[') {
                    $state.index++
                    $items = [System.Collections.Generic.List[object]]::new()
                    & $skip
                    if ($state.index -lt $text.Length -and $text[$state.index] -eq ']') {
                        $state.index++
                        return ,([object[]]$items.ToArray())
                    }
                    while ($true) {
                        $null = $items.Add((& $parseValue))
                        & $skip
                        if ($state.index -lt $text.Length -and $text[$state.index] -eq ']') {
                            $state.index++
                            return ,([object[]]$items.ToArray())
                        }
                        if ($state.index -ge $text.Length -or $text[$state.index] -ne ',') {
                            & $fail "Expected ',' or ']'."
                        }
                        $state.index++
                    }
                }
                if ($character -eq '{') {
                    $state.index++
                    $properties = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
                    & $skip
                    if ($state.index -lt $text.Length -and $text[$state.index] -eq '}') {
                        $state.index++
                        return $properties
                    }
                    while ($true) {
                        & $skip
                        $name = & $parseString
                        if ($properties.ContainsKey($name)) {
                            & $fail "Duplicate property '$name'."
                        }
                        & $skip
                        if ($state.index -ge $text.Length -or $text[$state.index] -ne ':') {
                            & $fail "Expected ':' after property name."
                        }
                        $state.index++
                        $properties.Add($name, (& $parseValue))
                        & $skip
                        if ($state.index -lt $text.Length -and $text[$state.index] -eq '}') {
                            $state.index++
                            return $properties
                        }
                        if ($state.index -ge $text.Length -or $text[$state.index] -ne ',') {
                            & $fail "Expected ',' or '}'."
                        }
                        $state.index++
                    }
                }
                foreach ($literal in @(@('true', $true), @('false', $false), @('null', $null))) {
                    if ($text.Length - $state.index -ge $literal[0].Length -and
                        $text.Substring($state.index, $literal[0].Length) -ceq $literal[0]) {
                        $state.index += $literal[0].Length
                        return $literal[1]
                    }
                }
                $match = [regex]::Match($text.Substring($state.index), '^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?')
                if (-not $match.Success) {
                    & $fail 'Invalid value.'
                }
                $state.index += $match.Length
                return @{ __rukbat_json_number = $match.Value }
            }
            $node = & $parseValue
            & $skip
            if ($state.index -ne $text.Length) {
                & $fail 'Unexpected trailing content.'
            }
            $convert = $null
            $convert = {
                param([object]$value, [hashtable]$type, [string]$path)
                if ($null -eq $value) {
                    if ($type.kind -eq 'nullable') { return $null }
                    throw "${path}: JSON null is incompatible with $($type.name)."
                }
                if ($type.kind -eq 'nullable') {
                    return (& $convert $value $type.element $path)
                }
                switch ($type.kind) {
                    'string' {
                        if ($value -is [string]) { return $value }
                    }
                    'bool' {
                        if ($value -is [bool]) { return $value }
                    }
                    'int' {
                        if ($value -is [hashtable] -and $value.ContainsKey('__rukbat_json_number') -and
                            $value.__rukbat_json_number -notmatch '[.eE]') {
                            $result = 0
                            if ([int]::TryParse($value.__rukbat_json_number,
                                [System.Globalization.NumberStyles]::AllowLeadingSign,
                                [System.Globalization.CultureInfo]::InvariantCulture, [ref]$result)) {
                                return $result
                            }
                        }
                    }
                    'float' {
                        if ($value -is [hashtable] -and $value.ContainsKey('__rukbat_json_number')) {
                            $result = [double]0
                            if ([double]::TryParse($value.__rukbat_json_number,
                                [System.Globalization.NumberStyles]::Float,
                                [System.Globalization.CultureInfo]::InvariantCulture, [ref]$result) -and
                                -not [double]::IsNaN($result) -and -not [double]::IsInfinity($result)) {
                                return $result
                            }
                        }
                    }
                    'array' {
                        if ($value -is [array]) {
                            $copy = [System.Collections.Generic.List[object]]::new($value.Length)
                            for ($i = 0; $i -lt $value.Length; $i++) {
                                $copy.Add((& $convert $value[$i] $type.element "$path[$i]")) > $null
                            }
                            return ,$copy
                        }
                    }
                    'struct' {
                        if ($value -is [System.Collections.Generic.Dictionary[string,object]]) {
                            $copy = @{}
                            foreach ($field in $type.fields) {
                                if (-not $value.ContainsKey($field.name)) {
                                    throw "$path.$($field.name): Missing required property."
                                }
                                $copy[$field.key] = & $convert $value[$field.name] $field.type "$path.$($field.name)"
                            }
                            return $copy
                        }
                    }
                }
                throw "${path}: JSON value is incompatible with $($type.name)."
            }
            return (& $convert $node $schema '$')
        }
        $__rukbat_json_encode = {
            param([object]$value, [hashtable]$schema)
            $quote = {
                param([string]$text)
                $builder = [System.Text.StringBuilder]::new()
                $null = $builder.Append([char]34)
                foreach ($character in $text.ToCharArray()) {
                    $escaped = switch ([int]$character) {
                        8 { '\b'; break }
                        9 { '\t'; break }
                        10 { '\n'; break }
                        12 { '\f'; break }
                        13 { '\r'; break }
                        34 { '\"'; break }
                        92 { '\\'; break }
                        default {
                            if ([int]$character -lt 32) { '\u' + ([int]$character).ToString('x4') }
                            else { [string]$character }
                        }
                    }
                    $null = $builder.Append($escaped)
                }
                $null = $builder.Append([char]34)
                return $builder.ToString()
            }
            $encode = $null
            $encode = {
                param([object]$item, [hashtable]$type)
                if ($null -eq $item) {
                    if ($type.kind -eq 'nullable') { return 'null' }
                    throw "Null is incompatible with $($type.name)."
                }
                if ($type.kind -eq 'nullable') { return (& $encode $item $type.element) }
                switch ($type.kind) {
                    'string' { return (& $quote ([string]$item)) }
                    'bool' { return $(if ($item) { 'true' } else { 'false' }) }
                    'int' { return ([int]$item).ToString([System.Globalization.CultureInfo]::InvariantCulture) }
                    'float' {
                        $number = [double]$item
                        if ([double]::IsNaN($number) -or [double]::IsInfinity($number)) {
                            throw 'Cannot encode a non-finite float as JSON.'
                        }
                        return $number.ToString('R', [System.Globalization.CultureInfo]::InvariantCulture)
                    }
                    'array' {
                        $parts = [System.Collections.Generic.List[string]]::new()
                        foreach ($element in $item) { $null = $parts.Add((& $encode $element $type.element)) }
                        return '[' + [string]::Join(',', $parts) + ']'
                    }
                    'struct' {
                        $parts = [System.Collections.Generic.List[string]]::new()
                        foreach ($field in $type.fields) {
                            $parts.Add((& $quote $field.name) + ':' + (& $encode $item[$field.key] $field.type)) > $null
                        }
                        return '{' + [string]::Join(',', $parts) + '}'
                    }
                }
                throw "Type $($type.name) cannot be encoded as JSON."
            }
            return (& $encode $value $schema)
        }
        """;
        foreach (var line in runtime.Split('\n'))
        {
            Line(line.TrimEnd('\r'));
        }
    }
}
