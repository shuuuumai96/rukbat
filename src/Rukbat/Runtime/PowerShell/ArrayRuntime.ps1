'array.slice' {
    $items = $values[0]
    $start = [long]$values[1]
    $count = [long]$values[2]
    if ($start -lt 0 -or $start -gt $items.Count -or $count -lt 0 -or $count -gt $items.Count - $start) {
        throw 'Array slice is outside the array range.'
    }
    $result = [object[]]::new([int]$count)
    for ($i = 0; $i -lt $count; $i++) { $result[$i] = $items[[int]($start + $i)] }
    return ,$result
}
'array.remove_at' {
    $items = $values[0]
    $index = [long]$values[1]
    if ($index -lt 0 -or $index -ge $items.Count) { throw 'Array removal index is outside the array range.' }
    $result = [object[]]::new($items.Count - 1)
    $write = 0
    for ($i = 0; $i -lt $items.Count; $i++) {
        if ($i -ne $index) { $result[$write] = $items[$i]; $write++ }
    }
    return ,$result
}
{ $_ -cin @('array.sort', 'array.sort_descending', 'array.sort_by', 'array.sort_by_descending') } {
    $items = $values[0]
    $keys = $items
    if ($operation -cin @('array.sort_by', 'array.sort_by_descending')) { $keys = $values[1] }
    if ($items.Count -ne $keys.Count) { throw 'Sort keys must have the same length as the values.' }
    $sortKeys = [object[]]::new($keys.Count)
    for ($i = 0; $i -lt $keys.Count; $i++) {
        $key = $keys[$i]
        # The checker restricts keys to primitive orderable values and opaque datetime values.
        if ($key -is [long] -or $key -is [double] -or $key -is [string]) { $sortKeys[$i] = $key }
        else { $sortKeys[$i] = $key.Ticks }
    }
    return ,([RukbatArray]::Sort([object[]]$items, $sortKeys, $operation.EndsWith('_descending', [StringComparison]::Ordinal)))
}
