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
        public sealed class RukbatJsonNumber { public readonly string Text; public RukbatJsonNumber(string text) { Text = text; } }
        public sealed class RukbatJsonValue {
            public object Node { get; private set; }
            public RukbatJsonValue(object node) { Node = node; }
            public string Kind {
                get {
                    if (Node == null) return "null";
                    if (Node is string) return "string";
                    if (Node is bool) return "bool";
                    if (Node is RukbatJsonNumber) return "number";
                    if (Node is object[]) return "array";
                    return "object";
                }
            }
            private Dictionary<string, object> Object() {
                var properties = Node as Dictionary<string, object>;
                if (properties == null) throw new Exception("JSON operation requires an object, got " + Kind + ".");
                return properties;
            }
            public RukbatJsonValue Get(string name) {
                object value;
                if (!Object().TryGetValue(name, out value)) throw new Exception("JSON object has no property '" + name + "'.");
                return new RukbatJsonValue(value);
            }
            public bool Has(string name) { return Object().ContainsKey(name); }
            public string[] Keys() {
                var properties = Object();
                var keys = new string[properties.Count];
                properties.Keys.CopyTo(keys, 0);
                Array.Sort(keys, StringComparer.Ordinal);
                return keys;
            }
            public RukbatJsonValue[] Items() {
                var items = ArrayItems();
                var values = new RukbatJsonValue[items.Length];
                for (var index = 0; index < items.Length; index++) values[index] = new RukbatJsonValue(items[index]);
                return values;
            }
            private object[] ArrayItems() {
                var items = Node as object[];
                if (items == null) throw new Exception("JSON operation requires an array, got " + Kind + ".");
                return items;
            }
            private static void CheckIndex(object[] items, int index) {
                if (index < 0 || index >= items.Length) throw new Exception("JSON array index " + index + " is out of range for " + items.Length + " items.");
            }
            public RukbatJsonValue Set(string name, RukbatJsonValue value) {
                var properties = new Dictionary<string, object>(Object(), StringComparer.Ordinal);
                properties[name] = value.Node;
                return new RukbatJsonValue(properties);
            }
            public RukbatJsonValue Remove(string name) {
                var properties = new Dictionary<string, object>(Object(), StringComparer.Ordinal);
                properties.Remove(name);
                return new RukbatJsonValue(properties);
            }
            public RukbatJsonValue SetAt(int index, RukbatJsonValue value) {
                var items = ArrayItems(); CheckIndex(items, index);
                var copy = (object[])items.Clone(); copy[index] = value.Node;
                return new RukbatJsonValue(copy);
            }
            public RukbatJsonValue Append(RukbatJsonValue value) {
                var items = ArrayItems();
                var copy = new object[items.Length + 1];
                Array.Copy(items, copy, items.Length); copy[items.Length] = value.Node;
                return new RukbatJsonValue(copy);
            }
            public RukbatJsonValue RemoveAt(int index) {
                var items = ArrayItems(); CheckIndex(items, index);
                var copy = new object[items.Length - 1];
                Array.Copy(items, 0, copy, 0, index);
                Array.Copy(items, index + 1, copy, index, items.Length - index - 1);
                return new RukbatJsonValue(copy);
            }
            private static void Quote(System.Text.StringBuilder builder, string value) {
                builder.Append('"');
                foreach (char character in value) {
                    switch (character) {
                        case '"': builder.Append("\\\""); break;
                        case '\\': builder.Append("\\\\"); break;
                        case '\b': builder.Append("\\b"); break;
                        case '\f': builder.Append("\\f"); break;
                        case '\n': builder.Append("\\n"); break;
                        case '\r': builder.Append("\\r"); break;
                        case '\t': builder.Append("\\t"); break;
                        default:
                            if (character < 32 || char.IsSurrogate(character)) builder.Append("\\u").Append(((int)character).ToString("x4"));
                            else builder.Append(character);
                            break;
                    }
                }
                builder.Append('"');
            }
            public static string Quote(string value) {
                var builder = new System.Text.StringBuilder();
                Quote(builder, value);
                return builder.ToString();
            }
            private static void Write(System.Text.StringBuilder builder, object value) {
                if (value == null) { builder.Append("null"); return; }
                if (value is string) { Quote(builder, (string)value); return; }
                if (value is bool) { builder.Append((bool)value ? "true" : "false"); return; }
                var number = value as RukbatJsonNumber;
                if (number != null) { builder.Append(number.Text); return; }
                var items = value as object[];
                if (items != null) {
                    builder.Append('[');
                    for (var index = 0; index < items.Length; index++) {
                        if (index != 0) builder.Append(',');
                        Write(builder, items[index]);
                    }
                    builder.Append(']'); return;
                }
                var properties = (Dictionary<string, object>)value;
                var keys = new string[properties.Count];
                properties.Keys.CopyTo(keys, 0);
                Array.Sort(keys, StringComparer.Ordinal);
                builder.Append('{');
                for (var index = 0; index < keys.Length; index++) {
                    if (index != 0) builder.Append(',');
                    Quote(builder, keys[index]); builder.Append(':'); Write(builder, properties[keys[index]]);
                }
                builder.Append('}');
            }
            public string ToJson() {
                var builder = new System.Text.StringBuilder();
                Write(builder, Node);
                return builder.ToString();
            }
        }
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
            param([object]$text, [hashtable]$schema, [bool]$fromValue = $false)
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
