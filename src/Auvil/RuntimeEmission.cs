namespace Auvil;

internal sealed partial class Emitter
{
    // Keep PowerShell's stream enumeration at the boundary. Internally an array is one value.
    private void EmitRuntime()
    {
        const string runtime = """
$__auv_clone = {
    param([object]$value, [scriptblock]$recurse)
    if ($value -is [array]) {
        $copy = [object[]]::new($value.Length)
        for ($i = 0; $i -lt $value.Length; $i++) { $copy[$i] = & $recurse $value[$i] $recurse }
        return ,$copy
    }
    if ($value -is [hashtable]) {
        $copy = @{}
        foreach ($key in $value.Keys) { $copy[$key] = & $recurse $value[$key] $recurse }
        return $copy
    }
    return $value
}
$__auv_index = {
    param([object[]]$values, [int]$offset, [string]$location, [bool]$address = $false)
    if ($offset -lt 0 -or $offset -ge $values.Length) {
        throw "${location}: Array index $offset is outside length $($values.Length)."
    }
    if ($address) { return @{ array = $values; offset = $offset } }
    return ,$values[$offset]
}
$__auv_runtime = {
    param([hashtable]$schema, [string]$command, [object[]]$positional, [hashtable]$named, [string]$location)
    $ErrorActionPreference = 'Stop'
    $convert = {
        param([object]$value, [hashtable]$type, [string]$path, [scriptblock]$recurse)
        if ($null -eq $value) { throw "${path}: Null is incompatible with $($type.name)." }
        switch ($type.kind) {
            'array' {
                if ($value -isnot [array]) { throw "${path}: Value is incompatible with $($type.name); expected an array." }
                $copy = [object[]]::new($value.Length)
                for ($i = 0; $i -lt $value.Length; $i++) {
                    $copy[$i] = & $recurse $value[$i] $type.element "$path[$i]" $recurse
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
                                $fieldValue = $value[$key]; $found = $true; break
                            }
                        }
                    } else {
                        foreach ($property in $value.PSObject.Properties) {
                            if ($property.Name -ceq $field.name) {
                                $fieldValue = $property.Value; $found = $true; break
                            }
                        }
                    }
                    $fieldPath = "$path.$($field.name)"
                    if (-not $found) { throw "${fieldPath}: Missing required field." }
                    $copy[$field.key] = & $recurse $fieldValue $field.type $fieldPath $recurse
                }
                return $copy
            }
            'int' {
                $integral = $value -is [sbyte] -or $value -is [byte] -or $value -is [int16] -or $value -is [uint16] -or $value -is [int] -or $value -is [uint32] -or $value -is [long] -or $value -is [ulong]
                if ($integral -and [decimal]$value -ge [int]::MinValue -and [decimal]$value -le [int]::MaxValue) { return [int]$value }
            }
            'string' { if ($value -is [string]) { return $value } }
            'bool' { if ($value -is [bool]) { return $value } }
        }
        throw "${path}: Value is incompatible with $($type.name)."
    }
    $result = @(& $command @positional @named)
    if ($schema.kind -eq 'void') { return }
    $path = "${location}: PowerShell call '$command' result"
    if ($schema.kind -eq 'array') {
        $value = & $convert $result $schema $path $convert
        return ,$value
    }
    if ($result.Count -ne 1) {
        throw "${location}: PowerShell call '$command' expected one $($schema.name) result, got $($result.Count)."
    }
    return (& $convert $result[0] $schema $path $convert)
}
""";
        foreach (var line in runtime.Split('\n')) Line(line.TrimEnd('\r'));
    }

    private static string Schema(ValueType type)
    {
        if (type.Element is { } element)
            return $"@{{kind = 'array'; name = {Quote(type.Name)}; element = {Schema(element)}}}";
        if (type.Struct is { } structure)
        {
            var fields = structure.Fields.Select(f =>
                $"@{{name = {Quote(f.Key)}; key = {Quote(f.Value.Key)}; type = {Schema(f.Value.Type)}}}");
            return $"@{{kind = 'struct'; name = {Quote(type.Name)}; fields = @({string.Join("; ", fields)})}}";
        }
        return $"@{{kind = {Quote(type.Name)}; name = {Quote(type.Name)}}}";
    }
}
