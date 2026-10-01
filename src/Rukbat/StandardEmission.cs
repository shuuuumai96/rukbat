namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitStandardRuntime(IReadOnlySet<string> modules)
    {
        EmitLines(PathContextRuntime);
        if (modules.Contains("fs")) EmitLines(FileEncodingRuntime);
        if (modules.Contains("fs")) EmitNativeLoader("MoveBatch");
        if (modules.Contains("process")) EmitNativeLoader("ProcessJob");
        Line("$__rukbat_standard = {");
        indentation++;
        Line("param([string]$operation, [object[]]$values)");
        Line("$ErrorActionPreference = 'Stop'");
        Line("switch ($operation) {");
        indentation++;
        if (modules.Contains("fs"))
        {
            EmitLines(FilesRuntime);
            EmitLines(DirectoriesRuntime);
        }
        if (modules.Contains("path")) EmitLines(PathRuntime);
        if (modules.Contains("csv")) EmitLines(CsvRuntime);
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
    $__rukbat_reject_invalid_path_characters = {
        param([string]$value)
        $hasControlCharacter = @($value.ToCharArray() | Where-Object { [int][char]$_ -lt 32 }).Count -ne 0
        if ($value.IndexOfAny([char[]]'"<>|?*') -ge 0 -or $hasControlCharacter) {
            throw 'Path contains invalid characters.'
        }
    }.GetNewClosure()
    $__rukbat_resolve_path = {
        param([string]$value)
        if ([string]::IsNullOrWhiteSpace($value)) { throw 'Path must not be empty or whitespace.' }
        & $__rukbat_reject_invalid_path_characters $value
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
    $__rukbat_entry_attributes = {
        param([string]$path)
        try { return [System.IO.File]::GetAttributes($path) }
        catch [System.IO.FileNotFoundException] { return $null }
        catch [System.IO.DirectoryNotFoundException] { return $null }
    }.GetNewClosure()
    $__rukbat_reject_directory_delete = {
        param([string]$path)
        $trim = {
            param([string]$value)
            $root = [System.IO.Path]::GetPathRoot($value)
            while ($value.Length -gt $root.Length -and ($value.EndsWith('\') -or $value.EndsWith('/'))) {
                $value = $value.Substring(0, $value.Length - 1)
            }
            return $value
        }
        $target = & $trim $path
        $root = [System.IO.Path]::GetPathRoot($target)
        if ([string]::Equals($target, $root, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to delete a filesystem root.'
        }
        $working = & $trim ([System.IO.Path]::GetFullPath($__rukbat_working_dir))
        if ([string]::Equals($target, $working, [System.StringComparison]::OrdinalIgnoreCase) -or
            $working.StartsWith($target + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to delete the captured working directory or one of its ancestors.'
        }
    }.GetNewClosure()
    """;

    private void EmitLines(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var emitted = line.TrimEnd('\r');
            if (emitted.Trim() == "'@ | Out-Null") RawLine(emitted.Trim());
            else Line(emitted);
        }
    }

    private const string FilesRuntime = """
    'fs.list_files' {
        $directory = & $__rukbat_resolve_path ([string]$values[0])
        $pattern = [string]$values[1]
        if ([string]::IsNullOrWhiteSpace($pattern) -or $pattern.IndexOfAny([char[]]'\\/') -ge 0) {
            throw 'fs.list_files pattern must be a non-empty filename wildcard without directory separators.'
        }
        if (-not [System.IO.Directory]::Exists($directory)) {
            throw "Cannot access directory: $($values[0])"
        }
        $paths = [System.Collections.Generic.List[string]]::new()
        foreach ($file in Get-ChildItem -LiteralPath $directory -Filter $pattern -File -ErrorAction Stop) {
            $null = $paths.Add($file.FullName)
        }
        return ,([object[]]$paths)
    }
    'fs.list_files_recursive' {
        $root = & $__rukbat_resolve_path ([string]$values[0])
        $pattern = [string]$values[1]
        if ([string]::IsNullOrWhiteSpace($pattern) -or $pattern.IndexOfAny([char[]]'\\/') -ge 0) {
            throw 'fs.list_files_recursive pattern must be a non-empty filename wildcard without directory separators.'
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
    'fs.read_text' {
        $selected = & $__rukbat_file_encoding ([string]$values[1])
        $bytes = [System.IO.File]::ReadAllBytes((& $__rukbat_resolve_path ([string]$values[0])))
        if ($selected.name -eq 'windows-932') { return (& $__rukbat_decode_windows932 $bytes) }
        $offset = 0
        switch ($selected.name) {
            'utf-8' {
                if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) { $offset = 3 }
            }
            'utf-16le' {
                if ($bytes.Length -ge 2 -and $bytes[0] -eq 255 -and $bytes[1] -eq 254) { $offset = 2 }
                elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 254 -and $bytes[1] -eq 255) { throw 'file encoding utf-16le conflicts with a UTF-16BE BOM.' }
            }
            'utf-16be' {
                if ($bytes.Length -ge 2 -and $bytes[0] -eq 254 -and $bytes[1] -eq 255) { $offset = 2 }
                elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 255 -and $bytes[1] -eq 254) { throw 'file encoding utf-16be conflicts with a UTF-16LE BOM.' }
            }
        }
        return $selected.encoding.GetString($bytes, $offset, $bytes.Length - $offset)
    }
    'fs.write_text' {
        $selected = & $__rukbat_file_encoding ([string]$values[2])
        $path = & $__rukbat_resolve_path ([string]$values[0])
        $text = [string]$values[1]
        $bytes = $selected.encoding.GetBytes($text)
        $noBackup = [System.Management.Automation.Language.NullString]::Value
        $directory = [System.IO.Path]::GetDirectoryName($path)
        $tempPath = $null
        $stream = $null
        try {
            for ($attempt = 0; $attempt -lt 32; $attempt++) {
                $candidate = [System.IO.Path]::Combine($directory, '.rukbat-write-' + [System.IO.Path]::GetRandomFileName())
                try {
                    $stream = [System.IO.File]::Open($candidate, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
                    $tempPath = $candidate
                    break
                } catch [System.IO.IOException] {
                    if (-not [System.IO.File]::Exists($candidate)) { throw }
                }
            }
            if ($null -eq $tempPath) {
                throw 'fs.write_text could not create a unique temporary file.'
            }
            try {
                $stream.Write($bytes, 0, $bytes.Length)
                $stream.Flush($true)
            } finally {
                if ($null -ne $stream) {
                    try { $stream.Dispose() } finally { $stream = $null }
                }
            }
            if ([System.IO.File]::Exists($path)) {
                [System.IO.File]::Replace($tempPath, $path, $noBackup)
            } else {
                try {
                    [System.IO.File]::Move($tempPath, $path)
                } catch [System.IO.IOException] {
                    if (-not [System.IO.File]::Exists($path)) { throw }
                    [System.IO.File]::Replace($tempPath, $path, $noBackup)
                }
            }
            $tempPath = $null
        } finally {
            if ($null -ne $stream) { $stream.Dispose() }
            if ($null -ne $tempPath) {
                try { [System.IO.File]::Delete($tempPath) } catch { }
            }
        }
        return
    }
    'fs.move_files_batch' {
        $sources = [System.Collections.Generic.List[string]]::new()
        $destinations = [System.Collections.Generic.List[string]]::new()
        foreach ($value in $values[0]) { $sources.Add((& $__rukbat_resolve_path ([string]$value))) }
        foreach ($value in $values[1]) { $destinations.Add((& $__rukbat_resolve_path ([string]$value))) }
        $journal = & $__rukbat_resolve_path ([string]$values[2])
        & $__rukbat_load_MoveBatch
        [RukbatMoveBatch]::Run($sources.ToArray(), $destinations.ToArray(), $journal)
        return
    }
    'fs.recover_file_moves' {
        $journal = & $__rukbat_resolve_path ([string]$values[0])
        & $__rukbat_load_MoveBatch
        [RukbatMoveBatch]::Recover($journal)
        return
    }
    'fs.copy_file' {
        $source = & $__rukbat_resolve_path ([string]$values[0])
        $destination = & $__rukbat_resolve_path ([string]$values[1])
        if ([string]::Equals($source, $destination, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'fs.copy_file source and destination must differ.'
        }
        $sourceAttributes = & $__rukbat_entry_attributes $source
        if ($null -eq $sourceAttributes) { throw "Cannot find file: $($values[0])" }
        if (($sourceAttributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
            throw "Expected file, found directory: $($values[0])"
        }
        if ($null -ne (& $__rukbat_entry_attributes $destination)) {
            throw "Destination already exists: $($values[1])"
        }
        [System.IO.File]::Copy($source, $destination, $false)
        return
    }
    'fs.move_file' {
        $source = & $__rukbat_resolve_path ([string]$values[0])
        $destination = & $__rukbat_resolve_path ([string]$values[1])
        if ([string]::Equals($source, $destination, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'fs.move_file source and destination must differ.'
        }
        $sourceAttributes = & $__rukbat_entry_attributes $source
        if ($null -eq $sourceAttributes) { throw "Cannot find file: $($values[0])" }
        if (($sourceAttributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
            throw "Expected file, found directory: $($values[0])"
        }
        if ($null -ne (& $__rukbat_entry_attributes $destination)) {
            throw "Destination already exists: $($values[1])"
        }
        [System.IO.File]::Move($source, $destination)
        if ([System.IO.File]::Exists($source)) {
            throw "fs.move_file did not remove source: $($values[0])"
        }
        return
    }
    'fs.remove_file' {
        $path = & $__rukbat_resolve_path ([string]$values[0])
        $attributes = & $__rukbat_entry_attributes $path
        if ($null -eq $attributes) { return }
        if (($attributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
            throw "Expected file, found directory: $($values[0])"
        }
        [System.IO.File]::Delete($path)
        return
    }
    'fs.is_file' {
        try { return [System.IO.File]::Exists((& $__rukbat_resolve_path ([string]$values[0]))) }
        catch { return $false }
    }
    """;

    private const string FileEncodingRuntime = """
    $__rukbat_decode_windows932 = {
        param([byte[]]$bytes)
        if ($null -eq ('RukbatWindows932Decoder' -as [type])) {
            Add-Type -TypeDefinition @'
    using System;
    using System.ComponentModel;
    using System.Runtime.InteropServices;
    public static class RukbatWindows932Decoder {
        private const uint Windows932 = 932;
        private const uint RejectInvalidCharacters = 8;
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int MultiByteToWideChar(uint codePage, uint flags,
            byte[] input, int byteCount, IntPtr output, int charCount);
        public static string Decode(byte[] bytes) {
            if (bytes.Length == 0) return String.Empty;
            int length = MultiByteToWideChar(Windows932, RejectInvalidCharacters,
                bytes, bytes.Length, IntPtr.Zero, 0);
            if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "Invalid windows-932 input.");
            IntPtr buffer = Marshal.AllocHGlobal(checked(length * sizeof(char)));
            try {
                if (MultiByteToWideChar(Windows932, RejectInvalidCharacters,
                    bytes, bytes.Length, buffer, length) == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Invalid windows-932 input.");
                return Marshal.PtrToStringUni(buffer, length);
            } finally { Marshal.FreeHGlobal(buffer); }
        }
    }
    '@ | Out-Null
        }
        return [RukbatWindows932Decoder]::Decode($bytes)
    }.GetNewClosure()
    $__rukbat_file_encoding = {
        param([string]$name)
        switch -CaseSensitive ($name) {
            'utf-8' { return [pscustomobject]@{ name = 'utf-8'; encoding = [System.Text.UTF8Encoding]::new($false, $true) } }
            'utf-16le' { return [pscustomobject]@{ name = 'utf-16le'; encoding = [System.Text.UnicodeEncoding]::new($false, $false, $true) } }
            'utf-16be' { return [pscustomobject]@{ name = 'utf-16be'; encoding = [System.Text.UnicodeEncoding]::new($true, $false, $true) } }
            'windows-932' {
                return [pscustomobject]@{
                    name = 'windows-932'
                    encoding = [System.Text.Encoding]::GetEncoding(932, [System.Text.EncoderExceptionFallback]::new(), [System.Text.DecoderExceptionFallback]::new())
                }
            }
            default { throw "unsupported file encoding: $name" }
        }
    }.GetNewClosure()
    """;

    private const string DirectoriesRuntime = """
    'fs.create_dir_all' {
        $path = & $__rukbat_resolve_path ([string]$values[0])
        $attributes = & $__rukbat_entry_attributes $path
        if ($null -ne $attributes -and ($attributes -band [System.IO.FileAttributes]::Directory) -eq 0) {
            throw "Expected directory, found file: $($values[0])"
        }
        [System.IO.Directory]::CreateDirectory($path)
        return
    }
    'fs.is_dir' {
        try { return [System.IO.Directory]::Exists((& $__rukbat_resolve_path ([string]$values[0]))) }
        catch { return $false }
    }
    'fs.remove_dir' {
        $path = & $__rukbat_resolve_path ([string]$values[0])
        & $__rukbat_reject_directory_delete $path
        $attributes = & $__rukbat_entry_attributes $path
        if ($null -eq $attributes) { return }
        if (($attributes -band [System.IO.FileAttributes]::Directory) -eq 0) {
            throw "Expected directory, found file: $($values[0])"
        }
        [System.IO.Directory]::Delete($path, $false)
        return
    }
    'fs.remove_dir_all' {
        $path = & $__rukbat_resolve_path ([string]$values[0])
        & $__rukbat_reject_directory_delete $path
        $attributes = & $__rukbat_entry_attributes $path
        if ($null -eq $attributes) { return }
        if (($attributes -band [System.IO.FileAttributes]::Directory) -eq 0) {
            throw "Expected directory, found file: $($values[0])"
        }
        if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            [System.IO.Directory]::Delete($path, $false)
            return
        }
        $pending = [System.Collections.Generic.Stack[System.IO.DirectoryInfo]]::new()
        $directories = [System.Collections.Generic.List[System.IO.DirectoryInfo]]::new()
        $pending.Push([System.IO.DirectoryInfo]::new($path))
        while ($pending.Count -ne 0) {
            $current = $pending.Pop()
            $null = $directories.Add($current)
            foreach ($entry in $current.GetFileSystemInfos()) {
                if (($entry.Attributes -band [System.IO.FileAttributes]::Directory) -ne 0) {
                    if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                        [System.IO.Directory]::Delete($entry.FullName, $false)
                    } else {
                        $pending.Push([System.IO.DirectoryInfo]::new($entry.FullName))
                    }
                } else {
                    [System.IO.File]::Delete($entry.FullName)
                }
            }
        }
        for ($index = $directories.Count - 1; $index -ge 0; $index--) {
            [System.IO.Directory]::Delete($directories[$index].FullName, $false)
        }
        return
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
    'path.parent' {
        $value = [string]$values[0]
        if ([string]::IsNullOrWhiteSpace($value)) { throw 'Path must not be empty or whitespace.' }
        & $__rukbat_reject_invalid_path_characters $value
        $root = [System.IO.Path]::GetPathRoot($value)
        $rootLength = if ($null -eq $root) { 0 } else { $root.Length }
        while ($value.Length -gt $rootLength -and ($value.EndsWith('\') -or $value.EndsWith('/'))) {
            $value = $value.Substring(0, $value.Length - 1)
        }
        $normalisedRoot = if ($root -like '\\*' -and -not ($root.EndsWith('\') -or $root.EndsWith('/'))) {
            $root + [System.IO.Path]::DirectorySeparatorChar
        } else { $root }
        if ($value.Length -eq $rootLength -and $rootLength -ne 0) { return $normalisedRoot }
        $parent = [System.IO.Path]::GetDirectoryName($value)
        if ([string]::IsNullOrEmpty($parent)) { return '.' }
        if ([string]::Equals($parent, $root, [System.StringComparison]::OrdinalIgnoreCase)) { return $normalisedRoot }
        return $parent
    }
    'path.equal' {
        $left = & $__rukbat_resolve_path ([string]$values[0])
        $right = & $__rukbat_resolve_path ([string]$values[1])
        return [string]::Equals($left, $right, [System.StringComparison]::OrdinalIgnoreCase)
    }
    'path.find_duplicate' {
        $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($value in $values[0]) {
            $resolved = & $__rukbat_resolve_path ([string]$value)
            if (-not $seen.Add($resolved)) { return [string]$value }
        }
        return $null
    }
    """;

    private const string CsvRuntime = """
    'csv.parse' {
        $text = [string]$values[0]
        $records = [System.Collections.Generic.List[object]]::new()
        if ($text.Length -eq 0) { return ,([object[]]$records) }
        $data = @{
            record = [System.Collections.Generic.List[object]]::new()
            field = [System.Text.StringBuilder]::new()
            recordStarted = $false
        }
        $state = 'field'
        $finishField = {
            $null = $data.record.Add($data.field.ToString())
            $null = $data.field.Clear()
        }
        $finishRecord = {
            & $finishField
            $null = $records.Add($data.record)
            $data.record = [System.Collections.Generic.List[object]]::new()
            $data.recordStarted = $false
        }
        for ($index = 0; $index -lt $text.Length; $index++) {
            $character = $text[$index]
            switch ($state) {
                'field' {
                    if ($character -eq '"') {
                        $state = 'quoted'
                        $data.recordStarted = $true
                    } elseif ($character -eq ',') {
                        & $finishField
                        $data.recordStarted = $true
                    } elseif ($character -eq [char]10) {
                        & $finishRecord
                    } elseif ($character -eq [char]13) {
                        if ($index + 1 -ge $text.Length -or $text[$index + 1] -ne [char]10) {
                            throw "CSV at offset ${index}: Bare carriage return outside a quoted field."
                        }
                        $index++
                        & $finishRecord
                    } else {
                        $null = $data.field.Append($character)
                        $state = 'unquoted'
                        $data.recordStarted = $true
                    }
                    break
                }
                'unquoted' {
                    if ($character -eq '"') {
                        throw "CSV at offset ${index}: Quote is not allowed in an unquoted field."
                    } elseif ($character -eq ',') {
                        & $finishField
                        $state = 'field'
                    } elseif ($character -eq [char]10) {
                        & $finishRecord
                        $state = 'field'
                    } elseif ($character -eq [char]13) {
                        if ($index + 1 -ge $text.Length -or $text[$index + 1] -ne [char]10) {
                            throw "CSV at offset ${index}: Bare carriage return outside a quoted field."
                        }
                        $index++
                        & $finishRecord
                        $state = 'field'
                    } else {
                        $null = $data.field.Append($character)
                    }
                    break
                }
                'quoted' {
                    if ($character -eq '"') {
                        $state = 'after quote'
                    } else {
                        $null = $data.field.Append($character)
                    }
                    break
                }
                'after quote' {
                    if ($character -eq '"') {
                        $null = $data.field.Append('"')
                        $state = 'quoted'
                    } elseif ($character -eq ',') {
                        & $finishField
                        $state = 'field'
                    } elseif ($character -eq [char]10) {
                        & $finishRecord
                        $state = 'field'
                    } elseif ($character -eq [char]13) {
                        if ($index + 1 -ge $text.Length -or $text[$index + 1] -ne [char]10) {
                            throw "CSV at offset ${index}: Bare carriage return outside a quoted field."
                        }
                        $index++
                        & $finishRecord
                        $state = 'field'
                    } else {
                        throw "CSV at offset ${index}: Expected a separator or record end after a closing quote."
                    }
                    break
                }
            }
        }
        if ($state -eq 'quoted') { throw "CSV at offset $($text.Length): Unclosed quoted field." }
        if ($data.recordStarted) { & $finishRecord }
        return ,([object[]]$records)
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
        $timeout = $values[2]
        if ($null -ne $timeout -and [int]$timeout -le 0) {
            throw 'process timeout must be greater than zero.'
        }
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
        & $__rukbat_load_ProcessJob
        return ,([RukbatProcessJob]::Run($start.FileName, [string]::Join(' ', $quoted), $__rukbat_working_dir, $timeout))
    }
    """;
}
