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
    & $__rukbat_reject_directory_delete $path
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
    & $__rukbat_reject_directory_delete $path
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
