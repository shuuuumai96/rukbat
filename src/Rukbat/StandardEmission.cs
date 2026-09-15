namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitStandardRuntime(IReadOnlySet<string> modules)
    {
        Line("$__rukbat_standard = {");
        indentation++;
        Line("param([string]$operation, [object[]]$values)");
        Line("switch ($operation) {");
        indentation++;
        if (modules.Contains("files")) EmitLines(FilesRuntime);
        if (modules.Contains("path")) EmitLines(PathRuntime);
        if (modules.Contains("env")) EmitLines(EnvironmentRuntime);
        if (modules.Contains("process")) EmitLines(ProcessRuntime);
        if (modules.Contains("text")) EmitLines(TextRuntime);
        if (modules.Contains("stderr")) EmitLines("""
            'stderr.print' {
                [Console]::Error.WriteLine([string]$values[0])
                return
            }
            """);
        indentation--;
        Line("}");
        Line("throw \"Unknown standard-library operation '$operation'.\"");
        indentation--;
        Line("}");
    }

    private void EmitLines(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            Line(line.TrimEnd('\r'));
        }
    }

    private const string FilesRuntime = """
    'files.list' {
        if (-not [System.IO.Directory]::Exists([string]$values[0])) {
            throw "Cannot access directory: $($values[0])"
        }
        $names = @(Get-ChildItem -LiteralPath ([string]$values[0]) -Filter ([string]$values[1]) -File -Name)
        return ,([object[]]$names)
    }
    'files.read' {
        $encoding = [System.Text.UTF8Encoding]::new($false, $true)
        $bytes = [System.IO.File]::ReadAllBytes([string]$values[0])
        $offset = 0
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) {
            $offset = 3
        }
        return $encoding.GetString($bytes, $offset, $bytes.Length - $offset)
    }
    'files.write' {
        $encoding = [System.Text.UTF8Encoding]::new($false, $true)
        [System.IO.File]::WriteAllText([string]$values[0], [string]$values[1], $encoding)
        return
    }
    'files.exists' {
        return [System.IO.File]::Exists([string]$values[0])
    }
    """;

    private const string PathRuntime = """
    'path.join' {
        return [System.IO.Path]::Combine([string[]]$values[0])
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
        $start.FileName = [string]$values[0]
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
