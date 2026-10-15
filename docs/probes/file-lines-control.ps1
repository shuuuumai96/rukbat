# Design experiment only. This does not compile or execute a Rukbat program.
trap { [Console]::Error.WriteLine($_.Exception.ToString()); exit 1 }
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script:checks = 0
function Assert-Equal($actual, $expected, [string]$label) {
    if (($actual | ConvertTo-Json -Compress -Depth 9) -cne ($expected | ConvertTo-Json -Compress -Depth 9)) {
        throw "Failed: $label"
    }
    $script:checks++
}
function Baseline {
    try { try { return 42 } finally { throw 'close' } } catch { return 99 }
}
Assert-Equal @(Baseline) @(42,99) 'native return leaks output before finally'
function Return-Probe([bool]$failClose) {
    $result = $null
    :functionExit do {
        try {
            try { $result = 42; break functionExit }
            finally { if ($failClose) { throw 'close' } }
        } catch { $result = 99; break functionExit }
    } while ($false)
    return $result
}
Assert-Equal @(Return-Probe $false) @(42) 'success return'
Assert-Equal @(Return-Probe $true) @(99) 'catch replacement return'
function Nested-Probe {
    $result = $null
    :nestedExit do {
        try {
            try {
                try { $result = 42; break nestedExit }
                finally { $script:events.Add('inner'); throw 'inner close' }
            } catch { $script:events.Add('inner catch'); $result = 99; break nestedExit }
        } finally { $script:events.Add('outer') }
    } while ($false)
    return $result
}
$script:events = New-Object 'System.Collections.Generic.List[string]'
Assert-Equal @(Nested-Probe) @(99) 'nested output'
Assert-Equal @($script:events.ToArray()) @('inner','inner catch','outer') 'nested catch and cleanup order'
function Loop-Probe {
    :loopExit do {
        for ($i = 0; $i -lt 2; $i++) {
            try {
                for ($j = 0; $j -lt 3; $j++) {
                    if ($j -eq 0) { continue }
                    $script:events.Add("$i/$j")
                    break
                }
            } finally { $script:events.Add("close $i") }
        }
        break loopExit
    } while ($false)
}
$script:events.Clear()
$null = Loop-Probe
Assert-Equal @($script:events.ToArray()) @('0/1','close 0','1/1','close 1') 'ordinary break and continue'
function Array-Probe($value) {
    $result = $null
    :arrayExit do { try { $result = $value; break arrayExit } finally { } } while ($false)
    if ($null -eq $result) { return $null }
    return ,$result
}
Assert-Equal (Array-Probe $null) $null 'nullable array null'
$empty = New-Object 'System.Collections.Generic.List[object]'
$one = New-Object 'System.Collections.Generic.List[object]'
$one.Add(7)
$emptyResult = @(Array-Probe $empty)
$oneResult = @(Array-Probe $one)
Assert-Equal $emptyResult.Count 1 'empty array is one return object'
Assert-Equal $emptyResult[0].Count 0 'empty array content'
Assert-Equal $oneResult.Count 1 'one element array is one return object'
Assert-Equal $oneResult[0][0] 7 'one element array content'
function Void-Effect { $script:effect++ }
function Void-Probe {
    :voidExit do { try { $null = Void-Effect; break voidExit } finally { $script:effect++ } } while ($false)
    return
}
$script:effect=0
Assert-Equal @(Void-Probe).Count 0 'void output'
Assert-Equal $script:effect 2 'void return expression and cleanup'
function Fallthrough-Probe {
    $result = $null
    :fallthroughExit do {
        try { try { $result=42; break fallthroughExit } finally { throw 'close' } }
        catch { $script:events.Add('recovered') }
        $result=100; break fallthroughExit
    } while ($false)
    return $result
}
Assert-Equal @(Fallthrough-Probe) @(100) 'catch fallthrough does not emit cancelled return'
function Recursive-Probe([int]$n) {
    $result = $null
    :recursiveExit do {
        try {
            if ($n -eq 0) { $result=1; break recursiveExit }
            $result=1+(Recursive-Probe ($n-1)); break recursiveExit
        } finally { $script:events.Add("rec $n") }
    } while ($false)
    return $result
}
Assert-Equal @(Recursive-Probe 3) @(4) 'recursive function-local labels'
Write-Output ('PowerShell=' + $PSVersionTable.PSVersion + '; assertions=' + $script:checks)
