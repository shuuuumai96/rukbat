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
