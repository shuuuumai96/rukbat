'csv.parse' { return ,(& $__rukbat_csv_parse ([string]$values[0])) }
'csv.stringify' { return (& $__rukbat_csv_stringify $values[0]) }
'csv.parse_table' {
    $records = & $__rukbat_csv_parse ([string]$values[0])
    if ($records.Count -eq 0) { throw 'CSV table requires a header record.' }
    $columns = [string[]]$records[0]
    $data = [System.Collections.Generic.List[object]]::new()
    for ($index = 1; $index -lt $records.Count; $index++) { $data.Add($records[$index]) }
    $rows = [RukbatCsvRow]::CreateRows($columns, [object[]]$data, 2)
    return ,@((,$columns); (,$rows))
}
'csv.table' {
    $columns = [string[]]$values[0]
    $rows = [RukbatCsvRow]::CreateRows($columns, [object[]]$values[1], 1)
    return ,@((,$columns); (,$rows))
}
'csv.row' { return [RukbatCsvRow]::Create([string[]]$values[0], [string[]]$values[1]) }
'csv.get' { return $values[0].Get([string]$values[1]) }
'csv.has_column' { return [RukbatCsvRow]::HasColumn([string[]]$values[0], [string]$values[1]) }
'csv.set' { return $values[0].Set([string]$values[1], [string]$values[2]) }
'csv.stringify_table' {
    $columns = [string[]]$values[0]
    [RukbatCsvRow]::ValidateColumns($columns)
    $records = [System.Collections.Generic.List[object]]::new()
    $records.Add($columns)
    foreach ($row in $values[1]) { $records.Add($row.Fields($columns)) }
    return (& $__rukbat_csv_stringify $records)
}
