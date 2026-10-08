'http.request' { return ,([RukbatHttp]::Request($values[0], $values[1])) }
'http.options' { return ,([RukbatHttp]::Options($values[0])) }
'http.header_values' { return ,([RukbatHttp]::HeaderValues($values[0], $values[1])) }
'http.with_query' { return [RukbatHttp]::WithQuery($values[0], $values[1]) }
'http.text' {
    $selected = & $__rukbat_text_encoding $values[1] 'http.text'
    return $selected.encoding.GetString($values[0].CopyBytes())
}
{ $_ -cin @('http.send', 'http.download') } {
    $request = $values[0]
    $options = $values[$values.Length - 1]
    [RukbatHttp]::Validate($request, $options, ($operation -ceq 'http.download'))
    $bytes = $null
    if ($null -ne $request[3]) {
        $selected = & $__rukbat_text_encoding $request[3][1] 'http request'
        if ($selected.encoding -is [RukbatCodePageCodec]) {
            $bytes = $selected.encoding.GetBytes([string]$request[3][0], [RukbatHttp]::MemoryLimit)
        } else {
            if ($selected.encoding.GetByteCount([string]$request[3][0]) -gt [RukbatHttp]::MemoryLimit) { throw 'HTTP request body exceeded 64 MiB.' }
            $bytes = $selected.encoding.GetBytes([string]$request[3][0])
        }
    }
    if ($operation -ceq 'http.send') { return ,([RukbatHttp]::Send($request, $options, $bytes, $null, 0)) }
    return ,([RukbatHttp]::Send($request, $options, $null, (& $__rukbat_resolve_path $values[1]), $values[2]))
}
