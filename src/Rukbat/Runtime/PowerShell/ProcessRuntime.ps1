{ $_ -cin @('process.options', 'process.run') } {
    if ($operation -ceq 'process.options') {
        $timeout = $values[0]
        $encodingName = [string]$values[1]
    } else {
        $options = $values[2]
        $timeout = $options[0]
        $encodingName = [string]$options[1]
    }
    $selected = & $__rukbat_text_encoding $encodingName 'process'
    if ($null -ne $timeout -and [long]$timeout -le 0) {
        throw 'process timeout must be greater than zero.'
    }
    if ($null -ne $timeout -and [long]$timeout -gt [int]::MaxValue) {
        throw 'process timeout must not exceed 2147483647 milliseconds.'
    }
    if ($operation -ceq 'process.options') { return ,([object[]]@($timeout, $encodingName)) }
    $workingDirectory = $__rukbat_working_dir
    if ($null -ne $options[2]) { $workingDirectory = & $__rukbat_resolve_path ([string]$options[2]) }
    if (-not [IO.Directory]::Exists($workingDirectory)) { throw 'Child working directory must be an accessible existing directory.' }
    & $__rukbat_load_ProcessJob
    $names = [System.Collections.Generic.List[string]]::new()
    $environmentValues = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $options[3]) {
        $names.Add([string]$entry[0])
        $environmentValues.Add($entry[1])
    }
    $environmentBlock = [RukbatProcessJob]::EnvironmentBlock($names.ToArray(), $environmentValues.ToArray())
    $standardInput = $null
    if ($null -ne $options[4]) {
        $inputValue = $options[4]
        $inputEncoding = & $__rukbat_text_encoding ([string]$inputValue[1]) 'process input'
        $text = [string]$inputValue[0]
        $inputLimit = 64 * 1024 * 1024
        if ($inputEncoding.encoding -is [Text.Encoding]) {
            if ($inputEncoding.encoding.GetByteCount($text) -gt $inputLimit) { throw 'process input exceeded 64 MiB.' }
            $standardInput = $inputEncoding.encoding.GetBytes($text)
        } else {
            $standardInput = $inputEncoding.encoding.GetBytes($text, $inputLimit)
        }
    }
    $quoteArgument = {
        param([string]$argument)
        if ($argument.IndexOf([char]0) -ge 0) { throw 'Process arguments must not contain NUL.' }
        $builder = [System.Text.StringBuilder]::new()
        $null = $builder.Append([char]34)
        $slashes = 0
        foreach ($character in $argument.ToCharArray()) {
            if ($character -eq [char]92) {
                $slashes++
                continue
            }
            if ($character -eq [char]34) {
                $null = $builder.Append([string]::new([char]92, 2 * $slashes + 1))
                $null = $builder.Append([char]34)
            } else {
                if ($slashes -ne 0) {
                    $null = $builder.Append([string]::new([char]92, $slashes))
                }
                $null = $builder.Append($character)
            }
            $slashes = 0
        }
        if ($slashes -ne 0) {
            $null = $builder.Append([string]::new([char]92, 2 * $slashes))
        }
        $null = $builder.Append([char]34)
        return $builder.ToString()
    }
    $quoted = [System.Collections.Generic.List[string]]::new()
    foreach ($argument in [string[]]$values[1]) {
        $null = $quoted.Add((& $quoteArgument $argument))
    }
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $executable = [string]$values[0]
    if ([string]::IsNullOrWhiteSpace($executable)) { throw 'Executable must not be empty.' }
    if ($executable.IndexOf([char]0) -ge 0) { throw 'Executable must not contain NUL.' }
    if ($executable.IndexOfAny([char[]]'\\/:') -ge 0) {
        $start.FileName = & $__rukbat_resolve_path $executable
    } else {
        # Resolve bare names explicitly: CreateProcess otherwise consults mutable process state.
        $names = @($executable)
        if ([IO.Path]::GetExtension($executable) -eq '') { $names = @($executable + '.exe') }
        $childPath = [RukbatProcessJob]::EnvironmentValue($environmentBlock, 'PATH')
        $directories = @($__rukbat_working_dir) + @($childPath -split ';')
        $found = $null
        foreach ($directory in $directories) {
            if ([string]::IsNullOrWhiteSpace($directory)) { continue }
            try { $directory = & $__rukbat_resolve_path $directory.Trim('"') } catch { continue }
            foreach ($name in $names) {
                $candidate = [IO.Path]::Combine($directory, $name)
                if ([IO.File]::Exists($candidate)) { $found = $candidate; break }
            }
            if ($null -ne $found) { break }
        }
        if ($null -eq $found) { throw "Could not find executable '$executable'." }
        $start.FileName = $found
    }
    $result = [RukbatProcessJob]::RunBytes($start.FileName, [string]::Join(' ', $quoted), $workingDirectory, $timeout, $environmentBlock, $standardInput)
    $stdout = $selected.encoding.GetString([byte[]]$result[1])
    $stderr = $selected.encoding.GetString([byte[]]$result[2])
    return ,([object[]]@($result[0], [string]$stdout, [string]$stderr))
}
