'csv.parse' {
    try { return ,([RukbatCsv]::Parse([string]$values[0])) }
    catch [System.FormatException] { throw $_.Exception.GetBaseException().Message }
}
'csv.records' { return (& $__rukbat_describe_text_file $values[0] $values[1]) }
'csv.__open_records' { return [RukbatCsvCursor]::new((& $__rukbat_open_text_file $values[0])) }
'csv.rows' {
    $file = & $__rukbat_describe_text_file $values[0] $values[1]
    return @{ file = $file; required = [string[]]$values[2] }
}
'csv.__open_rows' {
    $source = $values[0]
    return [RukbatCsvRow+NamedCursor]::new((& $__rukbat_open_text_file $source.file), $source.required)
}
'csv.stringify' {
    try { return [RukbatCsv]::Stringify($values[0]) }
    catch [System.FormatException] { throw $_.Exception.GetBaseException().Message }
}
'csv.parse_table' {
    try { return ,([RukbatCsvRow]::ParseTable([string]$values[0])) }
    catch [System.FormatException] { throw $_.Exception.GetBaseException().Message }
}
'csv.table' {
    $columns = [string[]]$values[0]
    $rows = [RukbatCsvRow]::CreateRows($columns, $values[1])
    return ,@((,$columns); (,$rows))
}
'csv.row' { return [RukbatCsvRow]::Create([string[]]$values[0], $values[1]) }
'csv.get' { return $values[0].Get([string]$values[1]) }
'csv.has_column' { return [RukbatCsvRow]::HasColumn([string[]]$values[0], [string]$values[1]) }
'csv.set' { return $values[0].Set([string]$values[1], [string]$values[2]) }
'csv.stringify_table' {
    return [RukbatCsvRow]::StringifyTable([string[]]$values[0], $values[1])
}
