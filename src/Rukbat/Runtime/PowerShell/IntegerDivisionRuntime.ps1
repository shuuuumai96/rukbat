$__rukbat_int_div = {
    param([long]$left, [long]$right, [bool]$modulo)
    if ($modulo -and $right -eq -1) { return [long]0 }
    $remainder = [long]0
    $quotient = [Math]::DivRem($left, $right, [ref]$remainder)
    if ($modulo) { return $remainder }
    return $quotient
}
