$__rukbat_csv_parse = {
    param([string]$text)
    try { return ,([RukbatCsv]::Parse($text)) }
    catch [System.FormatException] { throw $_.Exception.GetBaseException().Message }
}.GetNewClosure()

$__rukbat_csv_stringify = {
    param([object]$records)
    $text = [System.Text.StringBuilder]::new()
    $recordNumber = 0
    foreach ($record in $records) {
        $recordNumber++
        if ($record.Count -eq 0) {
            throw "csv.stringify record $recordNumber must contain at least one field."
        }
        for ($index = 0; $index -lt $record.Count; $index++) {
            if ($index -ne 0) { $null = $text.Append(',') }
            $field = [string]$record[$index]
            if ($field.Length -eq 0 -or $field.IndexOfAny([char[]]",`"`r`n") -ge 0) {
                $null = $text.Append('"').Append($field.Replace('"', '""')).Append('"')
            } else {
                $null = $text.Append($field)
            }
        }
        $null = $text.Append("`r`n")
    }
    return $text.ToString()
}.GetNewClosure()
