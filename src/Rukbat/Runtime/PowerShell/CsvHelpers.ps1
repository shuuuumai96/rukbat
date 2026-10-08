$__rukbat_csv_parse = {
    param([string]$text)
    $records = [System.Collections.Generic.List[object]]::new()
    if ($text.Length -eq 0) { return ,([object[]]$records) }
    $data = @{
        record = [System.Collections.Generic.List[object]]::new()
        field = [System.Text.StringBuilder]::new()
        recordStarted = $false
    }
    $state = 'field'
    $finishField = {
        $null = $data.record.Add($data.field.ToString())
        $null = $data.field.Clear()
    }
    $finishRecord = {
        & $finishField
        $null = $records.Add($data.record)
        $data.record = [System.Collections.Generic.List[object]]::new()
        $data.recordStarted = $false
    }
    for ($index = 0; $index -lt $text.Length; $index++) {
        $character = $text[$index]
        switch ($state) {
            'field' {
                if ($character -eq '"') {
                    $state = 'quoted'
                    $data.recordStarted = $true
                } elseif ($character -eq ',') {
                    & $finishField
                    $data.recordStarted = $true
                } elseif ($character -eq [char]10) {
                    & $finishRecord
                } elseif ($character -eq [char]13) {
                    if ($index + 1 -ge $text.Length -or $text[$index + 1] -ne [char]10) {
                        throw "CSV at offset ${index}: Bare carriage return outside a quoted field."
                    }
                    $index++
                    & $finishRecord
                } else {
                    $null = $data.field.Append($character)
                    $state = 'unquoted'
                    $data.recordStarted = $true
                }
                break
            }
            'unquoted' {
                if ($character -eq '"') {
                    throw "CSV at offset ${index}: Quote is not allowed in an unquoted field."
                } elseif ($character -eq ',') {
                    & $finishField
                    $state = 'field'
                } elseif ($character -eq [char]10) {
                    & $finishRecord
                    $state = 'field'
                } elseif ($character -eq [char]13) {
                    if ($index + 1 -ge $text.Length -or $text[$index + 1] -ne [char]10) {
                        throw "CSV at offset ${index}: Bare carriage return outside a quoted field."
                    }
                    $index++
                    & $finishRecord
                    $state = 'field'
                } else {
                    $null = $data.field.Append($character)
                }
                break
            }
            'quoted' {
                if ($character -eq '"') {
                    $state = 'after quote'
                } else {
                    $null = $data.field.Append($character)
                }
                break
            }
            'after quote' {
                if ($character -eq '"') {
                    $null = $data.field.Append('"')
                    $state = 'quoted'
                } elseif ($character -eq ',') {
                    & $finishField
                    $state = 'field'
                } elseif ($character -eq [char]10) {
                    & $finishRecord
                    $state = 'field'
                } elseif ($character -eq [char]13) {
                    if ($index + 1 -ge $text.Length -or $text[$index + 1] -ne [char]10) {
                        throw "CSV at offset ${index}: Bare carriage return outside a quoted field."
                    }
                    $index++
                    & $finishRecord
                    $state = 'field'
                } else {
                    throw "CSV at offset ${index}: Expected a separator or record end after a closing quote."
                }
                break
            }
        }
    }
    if ($state -eq 'quoted') { throw "CSV at offset $($text.Length): Unclosed quoted field." }
    if ($data.recordStarted) { & $finishRecord }
    return ,([object[]]$records)
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
