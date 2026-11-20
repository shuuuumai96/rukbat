$__rukbat_describe_text_file = {
    param([string]$name, [string]$encoding)
    $path = & $__rukbat_resolve_path $name
    $selected = & $__rukbat_text_encoding $encoding 'file'
    & $__rukbat_load_FileText
    return [RukbatFileText]::Describe($path, $selected.name)
}.GetNewClosure()

$__rukbat_open_text_file = {
    param($source)
    $selected = & $__rukbat_text_encoding $source.Encoding 'file'
    $decoder = if ($selected.encoding -is [System.Text.Encoding]) {
        [RukbatFileText]::CreateDecoder($selected.encoding)
    } else { $selected.encoding.CreateDecoder() }
    return [RukbatFileText]::Open($source, $decoder)
}.GetNewClosure()
