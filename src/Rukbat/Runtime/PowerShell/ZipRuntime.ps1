'zip.create' {
    & $__rukbat_load_Zip
    [RukbatZip]::Create((& $__rukbat_resolve_path $values[0]), (& $__rukbat_resolve_path $values[1]))
    return
}
{ $_ -cin @('zip.options', 'zip.list', 'zip.extract') } {
    & $__rukbat_load_Zip
    if ($operation -ceq 'zip.options') { $options = @($values[0], [long]100000, [long]10737418240) }
    else { $options = $values[$values.Length - 1] }
    $selected = & $__rukbat_text_encoding $options[0] 'zip names'
    [RukbatZip]::ValidateOptions($selected.name, $options[1], $options[2])
    if ($operation -ceq 'zip.options') { return ,([object[]]$options) }
    $archive = & $__rukbat_resolve_path $values[0]
    if ($operation -ceq 'zip.list') {
        return ,([RukbatZip]::List($archive, $selected.encoding, $options[1], $options[2]))
    }
    [RukbatZip]::Extract($archive, (& $__rukbat_resolve_path $values[1]), $selected.encoding, $options[1], $options[2])
    return
}
