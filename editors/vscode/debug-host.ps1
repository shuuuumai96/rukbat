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
$pipe.Connect(15000)
$global:__rukbat_debug_reader = New-Object System.IO.StreamReader($pipe, [System.Text.Encoding]::UTF8)
$global:__rukbat_debug_writer = New-Object System.IO.StreamWriter($pipe, (New-Object System.Text.UTF8Encoding($false)))
$global:__rukbat_debug_writer.AutoFlush = $true

function global:Send-RukbatDebug($message) {
    $global:__rukbat_debug_writer.WriteLine((ConvertTo-Json -InputObject $message -Depth 40 -Compress))
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
    if ($null -ne $type.PSObject.Properties['dictionaryKey'] -and $null -ne $type.dictionaryKey) {
        $node.value = $type.name + ' (' + $value['entries'].Count + ')'
        if ($depth -lt 6 -and $budget.remaining -gt 0) {
            $children = New-Object 'System.Collections.Generic.List[object]'
            foreach ($key in $value['order']) {
                if ($children.Count -ge 100 -or $budget.remaining -le 0) { break }
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
    } elseif ($null -ne $type.element) {
        $node.value = $type.name + ' (' + $value.Count + ')'
        if ($depth -lt 6 -and $budget.remaining -gt 0) {
            $children = New-Object 'System.Collections.Generic.List[object]'
            for ($index = 0; $index -lt [Math]::Min($value.Count, 100) -and $budget.remaining -gt 0; $index++) {
                $child = Format-RukbatDebugValue $value[$index] $type.element ($depth + 1) $budget
                $child.name = '[' + $index + ']'
                $null = $children.Add($child)
            }
            $null = $children.Add(@{ name = 'length'; type = 'int'; value = $value.Count.ToString(); children = @(); isConst = $true })
            $node.children = $children.ToArray()
        }
    } elseif ($null -ne $type.fields) {
        $node.value = $type.name
        if ($depth -lt 6 -and $budget.remaining -gt 0) {
            $children = New-Object 'System.Collections.Generic.List[object]'
            foreach ($field in $type.fields) {
                if ($budget.remaining -le 0) { break }
                $child = Format-RukbatDebugValue $value[$field.storage] $field.type ($depth + 1) $budget
                $child.name = $field.name
                $null = $children.Add($child)
            }
            $node.children = $children.ToArray()
        }
    } elseif ($type.name -ceq 'string') {
        $text = [string]$value
        if ($text.Length -gt 4096) { $text = $text.Substring(0, 4096) + '...' }
        $node.value = ConvertTo-Json -InputObject $text -Compress
    } elseif ($type.name -ceq 'bool') {
        $node.value = ([bool]$value).ToString().ToLowerInvariant()
    } elseif ($type.name -ceq 'int') {
        # Standard-library fields may use Int32 storage even though their bound
        # Rukbat type is int. Inspection follows that type, not backend storage.
        $node.value = ([long]$value).ToString([System.Globalization.CultureInfo]::InvariantCulture)
    } elseif ($type.name -ceq 'float') {
        $node.value = ([double]$value).ToString('R', [System.Globalization.CultureInfo]::InvariantCulture)
    } else {
        # Opaque standard values have no public Rukbat fields. Do not invoke
        # arbitrary PowerShell property getters or methods while inspecting them.
        $node.value = '<' + $type.name + '>'
    }
    return $node
}

$global:__rukbat_debug_checkpoint = {
    $frames = $global:__rukbat_debug_frames
    $points = @()
    for ($index = $frames.Count - 1; $index -ge 0; $index--) { $points += $frames[$index].point }
    Send-RukbatDebug @{ event = 'checkpoint'; points = $points }
    $command = Read-RukbatDebug
    if ($command.command -eq 'inspect') {
        $snapshot = New-Object 'System.Collections.Generic.List[object]'
        $budget = @{ remaining = 2000 }
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
