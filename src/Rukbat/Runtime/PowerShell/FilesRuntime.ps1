'fs.stat' {
    $path = & $__rukbat_resolve_path ([string]$values[0])
    $attributes = [IO.File]::GetAttributes($path)
    if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'fs.stat does not follow reparse points.'
    }
    $isDirectory = ($attributes -band [IO.FileAttributes]::Directory) -ne 0
    if ($isDirectory) { $info = [IO.DirectoryInfo]::new($path) }
    else { $info = [IO.FileInfo]::new($path) }
    $info.Refresh()
    if (-not $info.Exists) { throw 'fs.stat: entry disappeared while reading metadata.' }
    if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'fs.stat does not follow reparse points.' }
    $size = $null
    if (-not $isDirectory) { $size = [long]$info.Length }
    return ,([object[]]@($isDirectory, $size, [RukbatDateTime]::FromUtcTicks($info.LastWriteTimeUtc.Ticks)))
}
'fs.list_files' {
    $directory = & $__rukbat_resolve_path ([string]$values[0])
    $pattern = [string]$values[1]
    if ([string]::IsNullOrWhiteSpace($pattern) -or $pattern.IndexOfAny([char[]]'\\/') -ge 0) {
        throw 'fs.list_files pattern must be a non-empty filename wildcard without directory separators.'
    }
    if (-not [System.IO.Directory]::Exists($directory)) {
        throw "Cannot access directory: $($values[0])"
    }
    $paths = [System.Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $directory -Filter $pattern -File -ErrorAction Stop) {
        $null = $paths.Add($file.FullName)
    }
    return ,([object[]]$paths)
}
'fs.list_files_recursive' {
    $root = & $__rukbat_resolve_path ([string]$values[0])
    $pattern = [string]$values[1]
    if ([string]::IsNullOrWhiteSpace($pattern) -or $pattern.IndexOfAny([char[]]'\\/') -ge 0) {
        throw 'fs.list_files_recursive pattern must be a non-empty filename wildcard without directory separators.'
    }
    $rootInfo = [System.IO.DirectoryInfo]::new($root)
    if (-not $rootInfo.Exists) {
        throw "Cannot access directory: $($values[0])"
    }
    $pending = [System.Collections.Generic.Stack[System.IO.DirectoryInfo]]::new()
    $pending.Push($rootInfo)
    $paths = [System.Collections.Generic.List[string]]::new()
    $excluded = [System.IO.FileAttributes]::Hidden -bor [System.IO.FileAttributes]::ReparsePoint
    while ($pending.Count -ne 0) {
        $current = $pending.Pop()
        foreach ($directory in $current.GetDirectories()) {
            if (($directory.Attributes -band $excluded) -eq 0) {
                $pending.Push($directory)
            }
        }
        foreach ($file in Get-ChildItem -LiteralPath $current.FullName -Filter $pattern -File -Force -ErrorAction Stop) {
            if (($file.Attributes -band $excluded) -eq 0) {
                $null = $paths.Add($file.FullName)
            }
        }
    }
    return ,([object[]]$paths)
}
'fs.read_text' {
    $selected = & $__rukbat_text_encoding ([string]$values[1]) 'file'
    $bytes = [System.IO.File]::ReadAllBytes((& $__rukbat_resolve_path ([string]$values[0])))
    $offset = 0
    switch ($selected.name) {
        'utf-8' {
            if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) { $offset = 3 }
        }
        'utf-16le' {
            if ($bytes.Length -ge 2 -and $bytes[0] -eq 255 -and $bytes[1] -eq 254) { $offset = 2 }
            elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 254 -and $bytes[1] -eq 255) { throw 'file encoding utf-16le conflicts with a UTF-16BE BOM.' }
        }
        'utf-16be' {
            if ($bytes.Length -ge 2 -and $bytes[0] -eq 254 -and $bytes[1] -eq 255) { $offset = 2 }
            elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 255 -and $bytes[1] -eq 254) { throw 'file encoding utf-16be conflicts with a UTF-16LE BOM.' }
        }
        'utf-32le' {
            if ($bytes.Length -ge 4 -and $bytes[0] -eq 255 -and $bytes[1] -eq 254 -and $bytes[2] -eq 0 -and $bytes[3] -eq 0) { $offset = 4 }
            elseif ($bytes.Length -ge 4 -and $bytes[0] -eq 0 -and $bytes[1] -eq 0 -and $bytes[2] -eq 254 -and $bytes[3] -eq 255) { throw 'file encoding utf-32le conflicts with a UTF-32BE BOM.' }
        }
        'utf-32be' {
            if ($bytes.Length -ge 4 -and $bytes[0] -eq 0 -and $bytes[1] -eq 0 -and $bytes[2] -eq 254 -and $bytes[3] -eq 255) { $offset = 4 }
            elseif ($bytes.Length -ge 4 -and $bytes[0] -eq 255 -and $bytes[1] -eq 254 -and $bytes[2] -eq 0 -and $bytes[3] -eq 0) { throw 'file encoding utf-32be conflicts with a UTF-32LE BOM.' }
        }
    }
    return $selected.encoding.GetString($bytes, $offset, $bytes.Length - $offset)
}
'fs.write_text' {
    $selected = & $__rukbat_text_encoding ([string]$values[2]) 'file'
    $path = & $__rukbat_resolve_path ([string]$values[0])
    $text = [string]$values[1]
    $bytes = $selected.encoding.GetBytes($text)
    $noBackup = [System.Management.Automation.Language.NullString]::Value
    $directory = [System.IO.Path]::GetDirectoryName($path)
    $tempPath = $null
    $stream = $null
    try {
        for ($attempt = 0; $attempt -lt 32; $attempt++) {
            $candidate = [System.IO.Path]::Combine($directory, '.rukbat-write-' + [System.IO.Path]::GetRandomFileName())
            try {
                $stream = [System.IO.File]::Open($candidate, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
                $tempPath = $candidate
                break
            } catch [System.IO.IOException] {
                if (-not [System.IO.File]::Exists($candidate)) { throw }
            }
        }
        if ($null -eq $tempPath) {
            throw 'fs.write_text could not create a unique temporary file.'
        }
        try {
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush($true)
        } finally {
            if ($null -ne $stream) {
                try { $stream.Dispose() } finally { $stream = $null }
            }
        }
        if ([System.IO.File]::Exists($path)) {
            [System.IO.File]::Replace($tempPath, $path, $noBackup)
        } else {
            try {
                [System.IO.File]::Move($tempPath, $path)
            } catch [System.IO.IOException] {
                if (-not [System.IO.File]::Exists($path)) { throw }
                [System.IO.File]::Replace($tempPath, $path, $noBackup)
            }
        }
        $tempPath = $null
    } finally {
        if ($null -ne $stream) { $stream.Dispose() }
        if ($null -ne $tempPath) {
            try { [System.IO.File]::Delete($tempPath) } catch { }
        }
    }
    return
}
'fs.append_text' {
    $selected = & $__rukbat_text_encoding ([string]$values[2]) 'file'
    $bytes = $selected.encoding.GetBytes([string]$values[1])
    $path = & $__rukbat_resolve_path ([string]$values[0])
    $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Append, [System.IO.FileAccess]::Write, [System.IO.FileShare]::Read)
    try {
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush($true)
    } finally {
        $stream.Dispose()
    }
    return
}
'fs.create_temp_file' {
    $directory = & $__rukbat_resolve_path ([string]$values[0])
    & $__rukbat_load_TemporaryEntries
    return [RukbatTemporaryEntries]::CreateFile($directory)
}
'fs.move_files_batch' {
    $sources = [System.Collections.Generic.List[string]]::new()
    $destinations = [System.Collections.Generic.List[string]]::new()
    foreach ($value in $values[0]) { $sources.Add((& $__rukbat_resolve_path ([string]$value))) }
    foreach ($value in $values[1]) { $destinations.Add((& $__rukbat_resolve_path ([string]$value))) }
    $journal = & $__rukbat_resolve_path ([string]$values[2])
    & $__rukbat_load_MoveBatch
    [RukbatMoveBatch]::Run($sources.ToArray(), $destinations.ToArray(), $journal)
    return
}
'fs.recover_file_moves' {
    $journal = & $__rukbat_resolve_path ([string]$values[0])
    & $__rukbat_load_MoveBatch
    [RukbatMoveBatch]::Recover($journal)
    return
}
'fs.copy_file' {
    $source = & $__rukbat_resolve_path ([string]$values[0])
    $destination = & $__rukbat_resolve_path ([string]$values[1])
    if ([string]::Equals($source, $destination, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'fs.copy_file source and destination must differ.'
    }
    $sourceAttributes = & $__rukbat_entry_attributes $source
    if ($null -eq $sourceAttributes) { throw "Cannot find file: $($values[0])" }
    if (($sourceAttributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
        throw "Expected file, found directory: $($values[0])"
    }
    if ($null -ne (& $__rukbat_entry_attributes $destination)) {
        throw "Destination already exists: $($values[1])"
    }
    [System.IO.File]::Copy($source, $destination, $false)
    return
}
'fs.move_file' {
    $source = & $__rukbat_resolve_path ([string]$values[0])
    $destination = & $__rukbat_resolve_path ([string]$values[1])
    if ([string]::Equals($source, $destination, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'fs.move_file source and destination must differ.'
    }
    $sourceAttributes = & $__rukbat_entry_attributes $source
    if ($null -eq $sourceAttributes) { throw "Cannot find file: $($values[0])" }
    if (($sourceAttributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
        throw "Expected file, found directory: $($values[0])"
    }
    if ($null -ne (& $__rukbat_entry_attributes $destination)) {
        throw "Destination already exists: $($values[1])"
    }
    [System.IO.File]::Move($source, $destination)
    if ([System.IO.File]::Exists($source)) {
        throw "fs.move_file did not remove source: $($values[0])"
    }
    return
}
'fs.remove_file' {
    $path = & $__rukbat_resolve_path ([string]$values[0])
    $attributes = & $__rukbat_entry_attributes $path
    if ($null -eq $attributes) { return }
    if (($attributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
        throw "Expected file, found directory: $($values[0])"
    }
    [System.IO.File]::Delete($path)
    return
}
'fs.is_file' {
    try { return [System.IO.File]::Exists((& $__rukbat_resolve_path ([string]$values[0]))) }
    catch { return $false }
}
