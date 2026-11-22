'fs.walk_files' {
    $directory = & $__rukbat_resolve_path $values[0]
    $pattern = [string]$values[1]
    if ([string]::IsNullOrWhiteSpace($pattern) -or $pattern.IndexOfAny([char[]]'\/:') -ge 0 -or $pattern -in @('.', '..')) {
        throw 'fs.walk_files requires a filename wildcard without directory separators.'
    }
    return @{ directory = $directory; pattern = $pattern }
}
'fs.__open_walk' {
    & $__rukbat_load_FileScopes
    return [RukbatFileScopes]::OpenWalk($values[0].directory, $values[0].pattern)
}
'fs.try_lock' {
    return (& $__rukbat_resolve_path $values[0])
}
'fs.__open_lock' {
    & $__rukbat_load_FileScopes
    return [RukbatFileScopes]::OpenLock($values[0])
}
'fs.stage_text' {
    $path = & $__rukbat_resolve_path $values[0]
    $selected = & $__rukbat_text_encoding $values[1] 'file'
    return @{ path = $path; codec = $selected.encoding }
}
'fs.__open_stage' {
    & $__rukbat_load_FileScopes
    return [RukbatFileScopes]::OpenStage($values[0].path, $values[0].codec)
}
'fs.write_chunk' {
    $writer = $values[0]
    try {
        $writer.Write($writer.Codec.GetBytes([string]$values[1]))
    } catch {
        $writer.Invalidate()
        throw
    }
    return
}
'fs.commit' { $values[0].Commit(); return }
'fs.error_kind' {
    $kind = $values[0]['__rukbat_error_kind']
    if ($null -eq $kind) { return 'other' }
    return $kind
}
