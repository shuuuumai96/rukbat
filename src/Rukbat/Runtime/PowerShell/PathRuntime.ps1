'path.join' {
    if ($values[0].Count -eq 0) { throw 'path.join requires at least one component.' }
    return [System.IO.Path]::Combine([string[]]$values[0])
}
'path.script_dir' {
    return $__rukbat_script_dir
}
'path.working_dir' {
    return $__rukbat_working_dir
}
'path.resolve' {
    $baseDirectory = & $__rukbat_path_absolute ([string]$values[0])
    $parts = & $__rukbat_path_parts ([string]$values[1])
    return (& $__rukbat_path_absolute (& $__rukbat_resolve_path $parts.value $baseDirectory))
}
'path.relative' {
    $basePath = & $__rukbat_path_parts (& $__rukbat_path_absolute ([string]$values[0]))
    $targetPath = & $__rukbat_path_parts (& $__rukbat_path_absolute ([string]$values[1]))
    if (-not [string]::Equals($basePath.root, $targetPath.root, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Relative paths require the same drive or UNC share.'
    }
    $baseParts = $basePath.value.Substring($basePath.root.Length).Split([char[]]@('\'), [StringSplitOptions]::RemoveEmptyEntries)
    $targetParts = $targetPath.value.Substring($targetPath.root.Length).Split([char[]]@('\'), [StringSplitOptions]::RemoveEmptyEntries)
    $common = 0
    while ($common -lt $baseParts.Length -and $common -lt $targetParts.Length -and
        [string]::Equals($baseParts[$common], $targetParts[$common], [StringComparison]::OrdinalIgnoreCase)) {
        $common++
    }
    $result = [System.Collections.Generic.List[string]]::new()
    for ($index = $common; $index -lt $baseParts.Length; $index++) { $result.Add('..') }
    for ($index = $common; $index -lt $targetParts.Length; $index++) { $result.Add($targetParts[$index]) }
    if ($result.Count -eq 0) { return '.' }
    return [string]::Join('\', $result.ToArray())
}
'path.is_absolute' {
    try { return (& $__rukbat_path_parts ([string]$values[0])).absolute }
    catch { return $false }
}
'path.stem' {
    $name = & $__rukbat_path_filename ([string]$values[0])
    return [System.IO.Path]::GetFileNameWithoutExtension($name)
}
'path.change_extension' {
    $value = [string]$values[0]
    $name = & $__rukbat_path_filename $value
    if ($name.Length -eq 0) { throw 'Changing an extension requires a filename.' }
    $extension = [string]$values[1]
    & $__rukbat_reject_invalid_path_characters $extension
    if ($extension.Contains('\') -or $extension.Contains('/') -or $extension.Contains(':') -or
        ($extension.Length -ne 0 -and (-not $extension.StartsWith('.') -or $extension -eq '.'))) {
        throw 'An extension must be empty or start with a dot and contain no path separators or colon.'
    }
    $replacement = if ($extension.Length -eq 0) {
        [System.IO.Path]::GetFileNameWithoutExtension($name)
    } else {
        [System.IO.Path]::ChangeExtension($name, $extension)
    }
    if ([string]::IsNullOrEmpty($replacement) -or $replacement -eq '.' -or $replacement -eq '..') {
        throw 'Changing an extension must leave a filename.'
    }
    return $value.Substring(0, $value.Length - $name.Length) + $replacement
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
