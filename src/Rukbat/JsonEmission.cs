using System.Text;

namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitJsonRuntime()
    {
        const string runtime = """
        Add-Type -TypeDefinition @'
        using System;
        using System.Collections.Generic;
        public sealed class RukbatJsonNumber { public string Text; public RukbatJsonNumber(string text) { Text = text; } }
        public static class RukbatJsonParser {
            private static string text;
            private static int index;
            private static void Fail(string message) { throw new Exception("JSON at offset " + index + ": " + message); }
            private static void Skip() { while (index < text.Length && (text[index] == ' ' || text[index] == '\t' || text[index] == '\n' || text[index] == '\r')) index++; }
            private static string String() {
                if (index >= text.Length || text[index] != '\"') Fail("Expected a string.");
                index++; var builder = new System.Text.StringBuilder();
                while (index < text.Length) {
                    char character = text[index++];
                    if (character == '\"') return builder.ToString();
                    if (character < 32) Fail("Unescaped control character in string.");
                    if (character != '\\') { builder.Append(character); continue; }
                    if (index >= text.Length) Fail("Incomplete string escape.");
                    char escape = text[index++];
                    if (escape == 'u') {
                        if (index + 4 > text.Length) Fail("Incomplete Unicode escape.");
                        var hex = text.Substring(index, 4);
                        for (var offset = 0; offset < hex.Length; offset++) if (!Uri.IsHexDigit(hex[offset])) Fail("Invalid Unicode escape.");
                        index += 4; builder.Append((char)Convert.ToInt32(hex, 16)); continue;
                    }
                    switch (escape) {
                        case '\"': builder.Append('\"'); break; case '\\': builder.Append('\\'); break; case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break; case 'f': builder.Append('\f'); break; case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break; case 't': builder.Append('\t'); break; default: Fail("Unsupported escape '\\" + escape + "'."); break;
                    }
                }
                Fail("Unterminated string."); return null;
            }
            private static object Number() {
                var start = index; if (text[index] == '-') index++;
                if (index >= text.Length) Fail("Invalid value.");
                if (text[index] == '0') index++;
                else if (text[index] >= '1' && text[index] <= '9') { index++; while (index < text.Length && text[index] >= '0' && text[index] <= '9') index++; }
                else Fail("Invalid value.");
                if (index < text.Length && text[index] == '.') { index++; var fraction = index; while (index < text.Length && text[index] >= '0' && text[index] <= '9') index++; if (index == fraction) Fail("Invalid value."); }
                if (index < text.Length && (text[index] == 'e' || text[index] == 'E')) { index++; if (index < text.Length && (text[index] == '+' || text[index] == '-')) index++; var exponent = index; while (index < text.Length && text[index] >= '0' && text[index] <= '9') index++; if (index == exponent) Fail("Invalid value."); }
                return new RukbatJsonNumber(text.Substring(start, index - start));
            }
            private static object Value() {
                Skip(); if (index >= text.Length) Fail("Expected a value."); char character = text[index];
                if (character == '\"') return String();
                if (character == '[') { index++; var items = new List<object>(); Skip(); if (index < text.Length && text[index] == ']') { index++; return items.ToArray(); } while (true) { items.Add(Value()); Skip(); if (index < text.Length && text[index] == ']') { index++; return items.ToArray(); } if (index >= text.Length || text[index] != ',') Fail("Expected ',' or ']'."); index++; } }
                if (character == '{') { index++; var properties = new Dictionary<string, object>(StringComparer.Ordinal); Skip(); if (index < text.Length && text[index] == '}') { index++; return properties; } while (true) { Skip(); var name = String(); if (properties.ContainsKey(name)) Fail("Duplicate property '" + name + "'."); Skip(); if (index >= text.Length || text[index] != ':') Fail("Expected ':' after property name."); index++; properties.Add(name, Value()); Skip(); if (index < text.Length && text[index] == '}') { index++; return properties; } if (index >= text.Length || text[index] != ',') Fail("Expected ',' or '}'."); index++; } }
                if (index + 4 <= text.Length && text.Substring(index, 4) == "true") { index += 4; return true; }
                if (index + 5 <= text.Length && text.Substring(index, 5) == "false") { index += 5; return false; }
                if (index + 4 <= text.Length && text.Substring(index, 4) == "null") { index += 4; return null; }
                return Number();
            }
            public static object Parse(string source) { text = source; index = 0; var value = Value(); Skip(); if (index != text.Length) Fail("Unexpected trailing content."); return value; }
        }
'@ | Out-Null
        $__rukbat_json_decode = {
            param([string]$text, [hashtable]$schema)
            $state = @{ index = 0 }
            function __rukbat_json_fail {
                param([string]$message)
                throw "JSON at offset $($state.index): $message"
            }
            function __rukbat_json_skip {
                while ($state.index -lt $text.Length -and
                    ($text[$state.index] -eq [char]32 -or $text[$state.index] -eq [char]9 -or
                     $text[$state.index] -eq [char]10 -or $text[$state.index] -eq [char]13)) {
                    $state.index++
                }
            }
            function __rukbat_json_parse_string {
                if ($state.index -ge $text.Length -or $text[$state.index] -ne [char]34) {
                    __rukbat_json_fail 'Expected a string.'
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
                        __rukbat_json_fail 'Unescaped control character in string.'
                    }
                    if ($character -ne [char]92) {
                        $null = $builder.Append($character)
                        continue
                    }
                    if ($state.index -ge $text.Length) {
                        __rukbat_json_fail 'Incomplete string escape.'
                    }
                    $escape = $text[$state.index]
                    $state.index++
                    if ($escape -eq 'u') {
                        if ($state.index + 4 -gt $text.Length) {
                            __rukbat_json_fail 'Incomplete Unicode escape.'
                        }
                        $hex = $text.Substring($state.index, 4)
                        if ($hex -notmatch '^[0-9A-Fa-f]{4}$') {
                            __rukbat_json_fail 'Invalid Unicode escape.'
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
                        default { __rukbat_json_fail "Unsupported escape '\$escape'." }
                    }
                    $null = $builder.Append($escaped)
                }
                __rukbat_json_fail 'Unterminated string.'
            }
            function __rukbat_json_parse_value {
                __rukbat_json_skip
                if ($state.index -ge $text.Length) {
                    __rukbat_json_fail 'Expected a value.'
                }
                $character = $text[$state.index]
                if ($character -eq [char]34) {
                    return (__rukbat_json_parse_string)
                }
                if ($character -eq '[') {
                    $state.index++
                    $items = [System.Collections.Generic.List[object]]::new()
                    __rukbat_json_skip
                    if ($state.index -lt $text.Length -and $text[$state.index] -eq ']') {
                        $state.index++
                        return ,([object[]]$items.ToArray())
                    }
                    while ($true) {
                        $null = $items.Add((__rukbat_json_parse_value))
                        __rukbat_json_skip
                        if ($state.index -lt $text.Length -and $text[$state.index] -eq ']') {
                            $state.index++
                            return ,([object[]]$items.ToArray())
                        }
                        if ($state.index -ge $text.Length -or $text[$state.index] -ne ',') {
                            __rukbat_json_fail "Expected ',' or ']'."
                        }
                        $state.index++
                    }
                }
                if ($character -eq '{') {
                    $state.index++
                    $properties = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
                    __rukbat_json_skip
                    if ($state.index -lt $text.Length -and $text[$state.index] -eq '}') {
                        $state.index++
                        return $properties
                    }
                    while ($true) {
                        __rukbat_json_skip
                        $name = __rukbat_json_parse_string
                        if ($properties.ContainsKey($name)) {
                            __rukbat_json_fail "Duplicate property '$name'."
                        }
                        __rukbat_json_skip
                        if ($state.index -ge $text.Length -or $text[$state.index] -ne ':') {
                            __rukbat_json_fail "Expected ':' after property name."
                        }
                        $state.index++
                        $properties.Add($name, (__rukbat_json_parse_value))
                        __rukbat_json_skip
                        if ($state.index -lt $text.Length -and $text[$state.index] -eq '}') {
                            $state.index++
                            return $properties
                        }
                        if ($state.index -ge $text.Length -or $text[$state.index] -ne ',') {
                            __rukbat_json_fail "Expected ',' or '}'."
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
                $start = $state.index
                if ($text[$state.index] -eq '-') {
                    $state.index++
                }
                if ($state.index -ge $text.Length) {
                    __rukbat_json_fail 'Invalid value.'
                }
                if ($text[$state.index] -eq '0') {
                    $state.index++
                }
                elseif ($text[$state.index] -ge '1' -and $text[$state.index] -le '9') {
                    $state.index++
                    while ($state.index -lt $text.Length -and
                        $text[$state.index] -ge '0' -and $text[$state.index] -le '9') {
                        $state.index++
                    }
                }
                else {
                    __rukbat_json_fail 'Invalid value.'
                }
                if ($state.index -lt $text.Length -and $text[$state.index] -eq '.') {
                    $state.index++
                    $fractionStart = $state.index
                    while ($state.index -lt $text.Length -and
                        $text[$state.index] -ge '0' -and $text[$state.index] -le '9') {
                        $state.index++
                    }
                    if ($state.index -eq $fractionStart) {
                        __rukbat_json_fail 'Invalid value.'
                    }
                }
                if ($state.index -lt $text.Length -and ($text[$state.index] -eq 'e' -or $text[$state.index] -eq 'E')) {
                    $state.index++
                    if ($state.index -lt $text.Length -and ($text[$state.index] -eq '+' -or $text[$state.index] -eq '-')) {
                        $state.index++
                    }
                    $exponentStart = $state.index
                    while ($state.index -lt $text.Length -and
                        $text[$state.index] -ge '0' -and $text[$state.index] -le '9') {
                        $state.index++
                    }
                    if ($state.index -eq $exponentStart) {
                        __rukbat_json_fail 'Invalid value.'
                    }
                }
                return @{ __rukbat_json_number = $text.Substring($start, $state.index - $start) }
            }
            $node = [RukbatJsonParser]::Parse($text)
            function __rukbat_json_convert {
                param([object]$value, [hashtable]$type, [string]$path)
                if ($null -eq $value) {
                    if ($type.kind -eq 'nullable') { return $null }
                    throw "${path}: JSON null is incompatible with $($type.name)."
                }
                if ($type.kind -eq 'nullable') {
                    return (__rukbat_json_convert $value $type.element $path)
                }
                switch ($type.kind) {
                    'string' {
                        if ($value -is [string]) { return $value }
                    }
                    'bool' {
                        if ($value -is [bool]) { return $value }
                    }
                    'int' {
                        if ($value -is [RukbatJsonNumber] -and $value.Text -notmatch '[.eE]') {
                            $result = 0
                            if ([int]::TryParse($value.Text,
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
                                -not [double]::IsNaN($result) -and -not [double]::IsInfinity($result)) {
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
            function __rukbat_json_encode_value {
                param([object]$item, [hashtable]$type)
                if ($null -eq $item) {
                    if ($type.kind -eq 'nullable') { return 'null' }
                    throw "Null is incompatible with $($type.name)."
                }
                if ($type.kind -eq 'nullable') { return (__rukbat_json_encode_value $item $type.element) }
                switch ($type.kind) {
                    'string' { return (__rukbat_json_quote ([string]$item)) }
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
                        foreach ($element in $item) { $null = $parts.Add((__rukbat_json_encode_value $element $type.element)) }
                        return '[' + [string]::Join(',', $parts) + ']'
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
""";
        foreach (var line in runtime.Split('\n'))
        {
            var emitted = line.TrimEnd('\r');
            if (emitted == "'@ | Out-Null")
            {
                RawLine(emitted);
            }
            else
            {
                Line(emitted);
            }
        }
    }
}
