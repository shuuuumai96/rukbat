param([Parameter(Mandatory = $true)][string]$PipeName,
      [Parameter(Mandatory = $true)][string]$Configuration)

# This host uses only Windows PowerShell 5.1 facilities. Protocol traffic has its
# own local pipe: program output and explicit exit codes retain their meaning.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$config = Get-Content -LiteralPath $Configuration -Raw -Encoding UTF8 | ConvertFrom-Json
$global:__rukbat_debug_map = Get-Content -LiteralPath $config.map -Raw -Encoding UTF8 | ConvertFrom-Json
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', $PipeName, [System.IO.Pipes.PipeDirection]::InOut)
$pipeConnectTimeoutMs = 15000
$pipe.Connect($pipeConnectTimeoutMs)
$global:__rukbat_debug_limits = @{
    maxCollectionChildren = 100
    maxDepth = 6
    maxTextLength = 4096
    maxInspectionNodes = 2000
    jsonDepth = 40
}
$global:__rukbat_debug_reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)
$global:__rukbat_debug_writer = New-Object System.IO.StreamWriter($pipe, (New-Object System.Text.UTF8Encoding($false)))
$global:__rukbat_debug_writer.AutoFlush = $true

function global:Send-RukbatDebug($message) {
    $global:__rukbat_debug_writer.WriteLine((ConvertTo-Json -InputObject $message -Depth $global:__rukbat_debug_limits.jsonDepth -Compress))
}

function global:Read-RukbatDebug {
    $line = $global:__rukbat_debug_reader.ReadLine()
    if ($null -eq $line) { exit 0 }
    return (ConvertFrom-Json -InputObject $line)
}

function global:Format-RukbatDebugValue($value, [string]$typeId, [int]$depth, $budget) {
    $type = $global:__rukbat_debug_map.types.PSObject.Properties[$typeId].Value
    $node = @{ type = $type.name; value = ''; children = @() }
    if ($null -eq $value) { $node.value = 'null'; return $node }
    if ($null -ne $type.nullable) {
        $node = Format-RukbatDebugValue $value $type.nullable $depth $budget
        $node.type = $type.name
        return $node
    }
    $budget.remaining--
    foreach ($entry in $global:__rukbat_debug_shape_formatters.GetEnumerator()) {
        $property = $type.PSObject.Properties[$entry.Key]
        if ($null -ne $property -and $null -ne $property.Value) {
            & $entry.Value $value $type $node $depth $budget
            return $node
        }
    }
    $formatter = $global:__rukbat_debug_scalar_formatters[$type.name]
    # Opaque values have no public fields. Inspection never calls their methods.
    $node.value = if ($null -ne $formatter) { & $formatter $value } else { '<' + $type.name + '>' }
    return $node
}

$global:__rukbat_debug_shape_formatters = [ordered]@{
    dictionaryKey = {
        param($value, $type, $node, $depth, $budget)
        $node.value = $type.name + ' (' + $value['entries'].Count + ')'
        if ($depth -lt $global:__rukbat_debug_limits.maxDepth -and $budget.remaining -gt 0) {
            $children = New-Object 'System.Collections.Generic.List[object]'
            foreach ($key in $value['order']) {
                if ($children.Count -ge $global:__rukbat_debug_limits.maxCollectionChildren -or $budget.remaining -le 0) { break }
                $child = Format-RukbatDebugValue $value['entries'][$key]['value'] $type.dictionaryValue ($depth + 1) $budget
                $keyType = $global:__rukbat_debug_map.types.PSObject.Properties[$type.dictionaryKey].Value
                $label = if ($keyType.name -ceq 'string') { ConvertTo-Json -InputObject ([string]$key) -Compress } else { ([long]$key).ToString([System.Globalization.CultureInfo]::InvariantCulture) }
                $child.name = '[' + $label + ']'
                $child.dictionaryKey = $key
                $null = $children.Add($child)
            }
            $null = $children.Add(@{ name = 'length'; type = 'int'; value = $value['entries'].Count.ToString(); children = @(); isConst = $true })
            $node.children = $children.ToArray()
        }
    }
    element = {
        param($value, $type, $node, $depth, $budget)
        $node.value = $type.name + ' (' + $value.Count + ')'
        if ($depth -lt $global:__rukbat_debug_limits.maxDepth -and $budget.remaining -gt 0) {
            $children = New-Object 'System.Collections.Generic.List[object]'
            for ($index = 0; $index -lt [Math]::Min($value.Count, $global:__rukbat_debug_limits.maxCollectionChildren) -and $budget.remaining -gt 0; $index++) {
                $child = Format-RukbatDebugValue $value[$index] $type.element ($depth + 1) $budget
                $child.name = '[' + $index + ']'
                $null = $children.Add($child)
            }
            $null = $children.Add(@{ name = 'length'; type = 'int'; value = $value.Count.ToString(); children = @(); isConst = $true })
            $node.children = $children.ToArray()
        }
    }
    fields = {
        param($value, $type, $node, $depth, $budget)
        $node.value = $type.name
        if ($depth -lt $global:__rukbat_debug_limits.maxDepth -and $budget.remaining -gt 0) {
            $children = New-Object 'System.Collections.Generic.List[object]'
            foreach ($field in $type.fields) {
                if ($budget.remaining -le 0) { break }
                $child = Format-RukbatDebugValue $value[$field.storage] $field.type ($depth + 1) $budget
                $child.name = $field.name
                $null = $children.Add($child)
            }
            $node.children = $children.ToArray()
        }
    }
    displayValues = {
        param($value, $type, $node)
        # Validated standard tags use compiler-provided labels, never property getters.
        $label = $type.displayValues.PSObject.Properties[[string]$value]
        $node.value = if ($null -ne $label) { [string]$label.Value } else { '<' + $type.name + '>' }
    }
}

$global:__rukbat_debug_scalar_formatters = [hashtable]::new([StringComparer]::Ordinal)
$global:__rukbat_debug_scalar_formatters['string'] = {
    param($value)
    $text = [string]$value
    $limit = $global:__rukbat_debug_limits.maxTextLength
    if ($text.Length -gt $limit) { $text = $text.Substring(0, $limit) + '...' }
    ConvertTo-Json -InputObject $text -Compress
}
$global:__rukbat_debug_scalar_formatters['bool'] = {
    param($value)
    ([bool]$value).ToString().ToLowerInvariant()
}
$global:__rukbat_debug_scalar_formatters['int'] = {
    param($value)
    # A bound int can have Int32 storage at a standard-library boundary.
    ([long]$value).ToString([System.Globalization.CultureInfo]::InvariantCulture)
}
$global:__rukbat_debug_scalar_formatters['float'] = {
    param($value)
    ([double]$value).ToString('R', [System.Globalization.CultureInfo]::InvariantCulture)
}


$global:__rukbat_debug_checkpoint = {
    $frames = $global:__rukbat_debug_frames
    $points = @()
    for ($index = $frames.Count - 1; $index -ge 0; $index--) { $points += $frames[$index].point }
    Send-RukbatDebug @{ event = 'checkpoint'; points = $points }
    $command = Read-RukbatDebug
    if ($command.command -eq 'inspect') {
        $snapshot = New-Object 'System.Collections.Generic.List[object]'
        $budget = @{ remaining = $global:__rukbat_debug_limits.maxInspectionNodes }
        for ($index = $frames.Count - 1; $index -ge 0; $index--) {
            $frame = $frames[$index]
            if ($frame.point -lt 0) { continue }
            $point = $global:__rukbat_debug_map.points[$frame.point]
            $variables = New-Object 'System.Collections.Generic.List[object]'
            foreach ($variable in $point.variables) {
                $reference = $frame.variables[$variable.storage]
                if ($null -eq $reference) { continue }
                $node = Format-RukbatDebugValue $reference.Value $variable.type 0 $budget
                $node.name = $variable.name
                $node.global = $variable.global
                $node.isConst = $variable.isConst
                $null = $variables.Add($node)
            }
            $null = $snapshot.Add(@{ point = $frame.point; variables = $variables.ToArray() })
        }
        Send-RukbatDebug @{ event = 'snapshot'; frames = $snapshot.ToArray() }
        $command = Read-RukbatDebug
    }
    if ($command.command -eq 'stop') { exit 0 }
}

try {
    if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') {
        throw 'Rukbat debugging requires FullLanguage mode.'
    }
    # Set-PSBreakpoint expands wildcards; the generated script path is literal.
    $breakpointScript = [System.Management.Automation.WildcardPattern]::Escape($config.script)
    foreach ($point in $global:__rukbat_debug_map.points) {
        Set-PSBreakpoint -Script $breakpointScript -Line $point.generatedLine -Action {
            & $global:__rukbat_debug_checkpoint
        } | Out-Null
    }
    Send-RukbatDebug @{ event = 'ready' }
    $null = Read-RukbatDebug
    Set-Location -LiteralPath $config.cwd
    $programArguments = [string[]]$config.args
    & $config.script @programArguments
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
} finally {
    $pipe.Dispose()
}
