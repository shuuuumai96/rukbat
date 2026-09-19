namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitStandardRuntime(IReadOnlySet<string> modules)
    {
        EmitLines(PathContextRuntime);
        Line("$__rukbat_standard = {");
        indentation++;
        Line("param([string]$operation, [object[]]$values)");
        Line("$ErrorActionPreference = 'Stop'");
        Line("switch ($operation) {");
        indentation++;
        if (modules.Contains("files")) EmitLines(FilesRuntime);
        if (modules.Contains("path")) EmitLines(PathRuntime);
        if (modules.Contains("env")) EmitLines(EnvironmentRuntime);
        if (modules.Contains("process")) EmitLines(ProcessRuntime);
        if (modules.Contains("text")) EmitLines(TextRuntime);
        indentation--;
        Line("}");
        Line("throw \"Unknown standard-library operation '$operation'.\"");
        indentation--;
        Line("}.GetNewClosure()");
    }

    private const string PathContextRuntime = """
    # Capture the entry script's filesystem location, never a later PowerShell location.
    $__rukbat_resolve_path = {
        param([string]$value)
        if ([string]::IsNullOrWhiteSpace($value)) { throw 'Path must not be empty or whitespace.' }
        if ($value -match '^[A-Za-z]:(?![\\/])') {
            throw 'Drive-relative paths are not supported; use an absolute or working-directory-relative path.'
        }
        if ($value -match '^[\\/](?![\\/])') {
            $value = [IO.Path]::Combine([IO.Path]::GetPathRoot($__rukbat_working_dir), $value.Substring(1))
        } elseif (-not [IO.Path]::IsPathRooted($value)) {
            $value = [IO.Path]::Combine($__rukbat_working_dir, $value)
        }
        return [IO.Path]::GetFullPath($value)
    }.GetNewClosure()
    """;

    private void EmitLines(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            Line(line.TrimEnd('\r'));
        }
    }

    private const string FilesRuntime = """
    'files.list' {
        $directory = & $__rukbat_resolve_path ([string]$values[0])
        if (-not [System.IO.Directory]::Exists($directory)) {
            throw "Cannot access directory: $($values[0])"
        }
        $names = @(Get-ChildItem -LiteralPath $directory -Filter ([string]$values[1]) -File -Name -ErrorAction Stop)
        return ,([object[]]$names)
    }
    'files.list_recursive' {
        $root = & $__rukbat_resolve_path ([string]$values[0])
        $pattern = [string]$values[1]
        if ([string]::IsNullOrWhiteSpace($pattern) -or $pattern.IndexOfAny([char[]]'\\/') -ge 0) {
            throw 'files.list_recursive pattern must be a non-empty filename wildcard without directory separators.'
        }
        $rootInfo = [System.IO.DirectoryInfo]::new($root)
        if (-not $rootInfo.Exists) {
            throw "Cannot access directory: $($values[0])"
        }
        $pending = [System.Collections.Generic.Stack[System.IO.DirectoryInfo]]::new()
        $pending.Push($rootInfo)
        $paths = [System.Collections.Generic.List[string]]::new()
        $excluded = [System.IO.FileAttributes]::Hidden -bor [System.IO.FileAttributes]::ReparsePoint
        while ($pending.Count -ne 0) {
            $current = $pending.Pop()
            foreach ($directory in $current.GetDirectories()) {
                if (($directory.Attributes -band $excluded) -eq 0) {
                    $pending.Push($directory)
                }
            }
            foreach ($file in Get-ChildItem -LiteralPath $current.FullName -Filter $pattern -File -Force -ErrorAction Stop) {
                if (($file.Attributes -band $excluded) -eq 0) {
                    $null = $paths.Add($file.FullName)
                }
            }
        }
        return ,([object[]]$paths)
    }
    'files.read' {
        $encoding = [System.Text.UTF8Encoding]::new($false, $true)
        $bytes = [System.IO.File]::ReadAllBytes((& $__rukbat_resolve_path ([string]$values[0])))
        $offset = 0
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) {
            $offset = 3
        }
        return $encoding.GetString($bytes, $offset, $bytes.Length - $offset)
    }
    'files.write' {
        $encoding = [System.Text.UTF8Encoding]::new($false, $true)
        [System.IO.File]::WriteAllText((& $__rukbat_resolve_path ([string]$values[0])), [string]$values[1], $encoding)
        return
    }
    'files.exists' {
        try { return [System.IO.File]::Exists((& $__rukbat_resolve_path ([string]$values[0]))) }
        catch { return $false }
    }
    """;

    private const string PathRuntime = """
    'path.join' {
        if ($values[0].Count -eq 0) { throw 'path.join requires at least one component.' }
        return [System.IO.Path]::Combine([string[]]$values[0])
    }
    'path.script_dir' {
        return $__rukbat_script_dir
    }
    'path.filename' {
        return [System.IO.Path]::GetFileName([string]$values[0])
    }
    'path.extension' {
        return [System.IO.Path]::GetExtension([string]$values[0])
    }
    """;

    private const string EnvironmentRuntime = """
    'env.get' {
        $name = [string]$values[0]
        $value = [System.Environment]::GetEnvironmentVariable($name, 'Process')
        if ($null -ne $value) { return $value }
        # .NET Framework returns null for both a missing variable and an inherited
        # variable whose native Windows value is empty. The environment table keeps
        # the key, which lets Rukbat preserve that distinction when reading it.
        $environment = [System.Environment]::GetEnvironmentVariables('Process')
        if ($environment.Contains($name)) { return '' }
        return $null
    }
    'env.set' {
        $name = [string]$values[0]
        $value = [string]$values[1]
        if ($value.Length -eq 0) {
            throw "env.set does not accept an empty value; use env.remove to delete '$name'."
        }
        [System.Environment]::SetEnvironmentVariable($name, $value, 'Process')
        return
    }
    'env.remove' {
        $environmentPath = 'Env:' + [string]$values[0]
        if (Test-Path -LiteralPath $environmentPath) {
            Remove-Item -LiteralPath $environmentPath
        }
        return
    }
    """;

    private const string ProcessRuntime = """
    'process.run' {
        $quoteArgument = {
            param([string]$argument)
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
        if ($executable.IndexOfAny([char[]]'\\/:') -ge 0) {
            $start.FileName = & $__rukbat_resolve_path $executable
        } else {
            # Resolve bare names explicitly: CreateProcess otherwise consults mutable process state.
            $names = @($executable)
            if ([IO.Path]::GetExtension($executable) -eq '') { $names = @($executable + '.exe') }
            $directories = @($__rukbat_working_dir) + @($env:PATH -split ';')
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
        $start.WorkingDirectory = $__rukbat_working_dir
        $start.Arguments = [string]::Join(' ', $quoted)
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $start.CreateNoWindow = $true
        $encoding = [System.Text.UTF8Encoding]::new($false, $true)
        $start.StandardOutputEncoding = $encoding
        $start.StandardErrorEncoding = $encoding
        $process = [System.Diagnostics.Process]::new()
        $process.StartInfo = $start
        try {
            if (-not $process.Start()) { throw "Could not start process '$($values[0])'." }
            $stdoutTask = $process.StandardOutput.ReadToEndAsync()
            $stderrTask = $process.StandardError.ReadToEndAsync()
            $process.WaitForExit()
            $stdout = $stdoutTask.GetAwaiter().GetResult()
            $stderr = $stderrTask.GetAwaiter().GetResult()
            return ,([object[]]@($process.ExitCode, $stdout, $stderr))
        } finally {
            $process.Dispose()
        }
    }
    """;
}
