'math.to_float' { return [Convert]::ToDouble([long]$values[0]) }
'math.to_int' {
    $value = [double]$values[0]
    # (double)long.MaxValue rounds to 2^63, so the upper bound is exclusive.
    $upperBound = 9223372036854775808.0
    if ([double]::IsNaN($value) -or [double]::IsInfinity($value) -or $value -lt -$upperBound -or
        $value -ge $upperBound -or [Math]::Truncate($value) -ne $value) {
        throw 'Expected a whole float within the int range.'
    }
    return [long]$value
}
'math.trunc' { return [Math]::Truncate([double]$values[0]) }
'math.floor' { return [Math]::Floor([double]$values[0]) }
'math.ceil' { return [Math]::Ceiling([double]$values[0]) }
