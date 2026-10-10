'fs.list_dirs' {
    $directory = & $__rukbat_resolve_path ([string]$values[0])
    $pattern = [string]$values[1]
    if ([string]::IsNullOrWhiteSpace($pattern) -or $pattern.IndexOfAny([char[]]'\\/') -ge 0) {
        throw 'fs.list_dirs pattern must be a non-empty directory-name wildcard without directory separators.'
    }
    if (-not [System.IO.Directory]::Exists($directory)) {
        throw "Cannot access directory: $($values[0])"
    }
    $paths = [System.Collections.Generic.List[string]]::new()
    foreach ($child in Get-ChildItem -LiteralPath $directory -Filter $pattern -Directory -Force -ErrorAction Stop) {
        if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) {
            $null = $paths.Add($child.FullName)
        }
    }
    return ,([object[]]$paths)
}
'fs.move_dir' {
    $source = & $__rukbat_resolve_path ([string]$values[0])
    $destination = & $__rukbat_resolve_path ([string]$values[1])
    & $__rukbat_reject_directory_mutation $source 'move'
    $source = $source.TrimEnd([char[]]'\\/')
    $destinationRoot = [System.IO.Path]::GetPathRoot($destination)
    while ($destination.Length -gt $destinationRoot.Length -and ($destination.EndsWith('\') -or $destination.EndsWith('/'))) {
        $destination = $destination.Substring(0, $destination.Length - 1)
    }
    $attributes = & $__rukbat_entry_attributes $source
    if ($null -eq $attributes) { throw "Cannot find directory: $($values[0])" }
    if (($attributes -band [System.IO.FileAttributes]::Directory) -eq 0) {
        throw "Expected directory, found file: $($values[0])"
    }
    if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'fs.move_dir requires an ordinary directory, not a reparse point.'
    }
    if ([string]::Equals($source, $destination, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'fs.move_dir source and destination must differ.'
    }
    if (-not [string]::Equals([System.IO.Path]::GetPathRoot($source), [System.IO.Path]::GetPathRoot($destination), [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'fs.move_dir requires the same drive or UNC share.'
    }
    if ($destination.StartsWith($source + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'fs.move_dir destination must not be inside its source.'
    }
    if ($null -ne (& $__rukbat_entry_attributes $destination)) {
        throw "Destination already exists: $($values[1])"
    }
    [System.IO.Directory]::Move($source, $destination)
    return
}
'fs.create_dir_all' {
    $path = & $__rukbat_resolve_path ([string]$values[0])
    $attributes = & $__rukbat_entry_attributes $path
    if ($null -ne $attributes -and ($attributes -band [System.IO.FileAttributes]::Directory) -eq 0) {
        throw "Expected directory, found file: $($values[0])"
    }
    [System.IO.Directory]::CreateDirectory($path)
    return
}
'fs.is_dir' {
    try { return [System.IO.Directory]::Exists((& $__rukbat_resolve_path ([string]$values[0]))) }
    catch { return $false }
}
'fs.remove_dir' {
    $path = & $__rukbat_resolve_path ([string]$values[0])
    & $__rukbat_reject_directory_mutation $path
    $attributes = & $__rukbat_entry_attributes $path
    if ($null -eq $attributes) { return }
    if (($attributes -band [System.IO.FileAttributes]::Directory) -eq 0) {
        throw "Expected directory, found file: $($values[0])"
    }
    [System.IO.Directory]::Delete($path, $false)
    return
}
'fs.remove_dir_all' {
    $path = & $__rukbat_resolve_path ([string]$values[0])
    & $__rukbat_reject_directory_mutation $path
    $attributes = & $__rukbat_entry_attributes $path
    if ($null -eq $attributes) { return }
    if (($attributes -band [System.IO.FileAttributes]::Directory) -eq 0) {
        throw "Expected directory, found file: $($values[0])"
    }
    if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        [System.IO.Directory]::Delete($path, $false)
        return
    }
    $pending = [System.Collections.Generic.Stack[System.IO.DirectoryInfo]]::new()
    $directories = [System.Collections.Generic.List[System.IO.DirectoryInfo]]::new()
    $pending.Push([System.IO.DirectoryInfo]::new($path))
    while ($pending.Count -ne 0) {
        $current = $pending.Pop()
        $null = $directories.Add($current)
        foreach ($entry in $current.GetFileSystemInfos()) {
            if (($entry.Attributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
                if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                    [System.IO.Directory]::Delete($entry.FullName, $false)
                } else {
                    $pending.Push([System.IO.DirectoryInfo]::new($entry.FullName))
                }
            } else {
                [System.IO.File]::Delete($entry.FullName)
            }
        }
    }
    for ($index = $directories.Count - 1; $index -ge 0; $index--) {
        [System.IO.Directory]::Delete($directories[$index].FullName, $false)
    }
    return
}
