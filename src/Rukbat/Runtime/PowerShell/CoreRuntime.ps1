$__rukbat_array = {
    param([object[]]$values)
    if ($null -eq $values -or $values.Length -eq 0) {
        return ,([System.Collections.Generic.List[object]]::new())
    }
    $array = [System.Collections.Generic.List[object]]::new($values.Length)
    $array.AddRange($values)
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
