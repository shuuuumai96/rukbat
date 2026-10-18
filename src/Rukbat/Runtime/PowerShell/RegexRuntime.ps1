're.compile' {
    & $__rukbat_load_Regex
    $values[0] = [string]$values[0]
    $values[1] = [long]$values[1]
    return [RukbatRegex]::Compile($values)
}
{ $_ -cin @('re.is_match', 're.find', 're.find_all', 're.replace_all', 're.replace_all_template', 're.split') } {
    & $__rukbat_load_Regex
    # Imported function results can carry PSObject wrappers. Normalize scalar
    # slots here, while keeping large strings inside the existing argument array.
    $values[0] = [string]$values[0]
    $values[1] = [RukbatRegexPattern]$values[1]
    if ($values.Length -gt 2) { $values[2] = [string]$values[2] }
    switch ($operation) {
        're.is_match' { return [RukbatRegex]::IsMatch($values) }
        're.replace_all' { return [RukbatRegex]::ReplaceAll($values) }
        're.replace_all_template' { return [RukbatRegex]::ReplaceAllTemplate($values) }
        're.split' { return ,([object[]][RukbatRegex]::Split($values)) }
    }
    if ($operation -ceq 're.find') {
        $found = [RukbatRegex]::Find($values)
        if ($null -eq $found) { return $null }
        $rows = ,$found
    } else { $rows = [RukbatRegex]::FindAll($values) }
    $result = [System.Collections.Generic.List[object]]::new()
    foreach ($row in $rows) {
        $groups = & $__rukbat_dictionary 'new' $null 'string' $null '' $null
        for ($index = 0; $index -lt $row[3].Length; $index++) {
            & $__rukbat_dictionary 'add' $groups $row[3][$index] $row[4][$index] '' $null
        }
        $result.Add([object[]]@($row[0], $row[1], $row[2], $groups))
    }
    if ($operation -ceq 're.find') { return ,$result[0] }
    return ,([object[]]$result.ToArray())
}
're.escape' {
    & $__rukbat_load_Regex
    $values[0] = [string]$values[0]
    return [RukbatRegex]::Escape($values)
}
