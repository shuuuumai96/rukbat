'path.join' {
    if ($values[0].Count -eq 0) { throw 'path.join requires at least one component.' }
    return [System.IO.Path]::Combine([string[]]$values[0])
}
'path.script_dir' {
    return $__rukbat_script_dir
}
'path.filename' {
    return [System.IO.Path]::GetFileName([string]$values[0])
}
'path.extension' {
    return [System.IO.Path]::GetExtension([string]$values[0])
}
'path.parent' {
    $value = [string]$values[0]
    if ([string]::IsNullOrWhiteSpace($value)) { throw 'Path must not be empty or whitespace.' }
    & $__rukbat_reject_invalid_path_characters $value
    $root = [System.IO.Path]::GetPathRoot($value)
    $rootLength = if ($null -eq $root) { 0 } else { $root.Length }
    while ($value.Length -gt $rootLength -and ($value.EndsWith('\') -or $value.EndsWith('/'))) {
        $value = $value.Substring(0, $value.Length - 1)
    }
    $normalisedRoot = if ($root -like '\\*' -and -not ($root.EndsWith('\') -or $root.EndsWith('/'))) {
        $root + [System.IO.Path]::DirectorySeparatorChar
    } else { $root }
    if ($value.Length -eq $rootLength -and $rootLength -ne 0) { return $normalisedRoot }
    $parent = [System.IO.Path]::GetDirectoryName($value)
    if ([string]::IsNullOrEmpty($parent)) { return '.' }
    if ([string]::Equals($parent, $root, [System.StringComparison]::OrdinalIgnoreCase)) { return $normalisedRoot }
    return $parent
}
'path.equal' {
    $left = & $__rukbat_resolve_path ([string]$values[0])
    $right = & $__rukbat_resolve_path ([string]$values[1])
    return [string]::Equals($left, $right, [System.StringComparison]::OrdinalIgnoreCase)
}
'path.find_duplicate' {
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($value in $values[0]) {
        $resolved = & $__rukbat_resolve_path ([string]$value)
        if (-not $seen.Add($resolved)) { return [string]$value }
    }
    return $null
}
