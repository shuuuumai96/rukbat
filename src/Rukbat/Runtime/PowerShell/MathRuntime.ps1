'math.to_float' { return [RukbatNumbers]::ToFloat([long]$values[0]) }
'math.to_int' { return [RukbatNumbers]::ToInt([double]$values[0]) }
'math.trunc' { return [Math]::Truncate([double]$values[0]) }
'math.floor' { return [Math]::Floor([double]$values[0]) }
'math.ceil' { return [Math]::Ceiling([double]$values[0]) }
