'hash.parse' { & $__rukbat_load_Hash; return [RukbatHash]::Parse($values[0]) }
'hash.name' { return [string]$values[0] }
'hash.file' {
    & $__rukbat_load_Hash
    return [RukbatHash]::File((& $__rukbat_resolve_path $values[0]), $values[1])
}
'hash.text' {
    & $__rukbat_load_Hash
    $selected = & $__rukbat_text_encoding $values[2] 'hash.text'
    return [RukbatHash]::Text($selected.encoding.GetBytes([string]$values[0]), $values[1])
}
