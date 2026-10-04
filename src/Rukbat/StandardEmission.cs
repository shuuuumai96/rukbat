namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitStandardRuntime(IReadOnlySet<string> modules)
    {
        EmitLines(PathContextRuntime);
        if (modules.Contains("encoding") || modules.Contains("fs") || modules.Contains("process"))
        {
            EmitNativeLoader("CodePageCodec");
            EmitEncodingSelector();
        }
        if (modules.Contains("datetime"))
        {
            EmitNativeLoader("DateTime");
            Line("& $__rukbat_load_DateTime");
        }
        if (modules.Contains("fs")) EmitNativeLoader("MoveBatch");
        if (modules.Contains("process")) EmitNativeLoader("ProcessJob");
        if (modules.Contains("csv")) EmitCsvRuntime();
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
        if (modules.Contains("json")) EmitLines(JsonValueRuntime);
        if (modules.Contains("env")) EmitLines(EnvironmentRuntime);
        if (modules.Contains("process")) EmitLines(ProcessRuntime);
        if (modules.Contains("text")) EmitLines(TextRuntime);
        if (modules.Contains("math")) EmitLines(MathRuntime);
        if (modules.Contains("datetime")) EmitDateTimeOperations();
        if (modules.Contains("encoding")) EmitEncodingOperations();
        indentation--;
        Line("}");
        Line("throw \"Unknown standard-library operation '$operation'.\"");
        indentation--;
        Line("}.GetNewClosure()");
    }

    private void EmitEncodingOperations()
    {
        Line("'encoding.parse' { return (& $__rukbat_text_encoding ([string]$values[0]) 'text').name }");
        Line("'encoding.from_code_page' {");
        Line("    $number = [int]$values[0]");
        Line("    switch ($number) {");
        foreach (var value in EncodingValues.All)
            Line($"        {value.CodePage} {{ return (& $__rukbat_text_encoding {Quote(value.Label)} 'text').name }}");
        Line("    }");
        Line("    return (& $__rukbat_text_encoding ('windows-' + $number) 'text').name");
        Line("}");
        Line("'encoding.name' { return [string]$values[0] }");
    }

    private void EmitEncodingSelector()
    {
        Line("$__rukbat_text_encoding = {");
        Line("    param([string]$name, [string]$operation)");
        Line("    & $__rukbat_load_CodePageCodec");
        Line("    switch -CaseSensitive ($name) {");
        foreach (var value in EncodingValues.All)
            Line($"        {Quote(value.Label)} {{ return [pscustomobject]@{{ name = {Quote(value.Label)}; encoding = {value.Codec} }} }}");
        Line("    }");
        Line("    if ($name -cmatch '^windows-([1-9][0-9]*)$') {");
        Line("        $number = 0");
        Line("        if ([int]::TryParse($Matches[1], [ref]$number)) {");
        Line("            switch ($number) {");
        foreach (var value in EncodingValues.All.Where(v => v.Label != $"windows-{v.CodePage}"))
            Line($"                {value.CodePage} {{ throw \"Use canonical encoding name {value.Label}.\" }}");
        Line("            }");
        Line("            try { return [pscustomobject]@{ name = $name; encoding = [RukbatCodePageCodec]::new($number) } }");
        Line("            catch { throw \"unsupported encoding: $name\" }");
        Line("        }");
        Line("    }");
        Line("    throw \"unsupported encoding: $name\"");
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
    'fs.stat' {
        $path = & $__rukbat_resolve_path ([string]$values[0])
        $attributes = [IO.File]::GetAttributes($path)
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'fs.stat does not follow reparse points.'
        }
        $isDirectory = ($attributes -band [IO.FileAttributes]::Directory) -ne 0
        if ($isDirectory) { $info = [IO.DirectoryInfo]::new($path) }
        else { $info = [IO.FileInfo]::new($path) }
        $info.Refresh()
        if (-not $info.Exists) { throw 'fs.stat: entry disappeared while reading metadata.' }
        if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'fs.stat does not follow reparse points.' }
        $size = $null
        if (-not $isDirectory) { $size = [long]$info.Length }
        return ,([object[]]@($isDirectory, $size, [RukbatDateTime]::FromUtcTicks($info.LastWriteTimeUtc.Ticks)))
    }
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
        $selected = & $__rukbat_text_encoding ([string]$values[1]) 'file'
        $bytes = [System.IO.File]::ReadAllBytes((& $__rukbat_resolve_path ([string]$values[0])))
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
            'utf-32le' {
                if ($bytes.Length -ge 4 -and $bytes[0] -eq 255 -and $bytes[1] -eq 254 -and $bytes[2] -eq 0 -and $bytes[3] -eq 0) { $offset = 4 }
                elseif ($bytes.Length -ge 4 -and $bytes[0] -eq 0 -and $bytes[1] -eq 0 -and $bytes[2] -eq 254 -and $bytes[3] -eq 255) { throw 'file encoding utf-32le conflicts with a UTF-32BE BOM.' }
            }
            'utf-32be' {
                if ($bytes.Length -ge 4 -and $bytes[0] -eq 0 -and $bytes[1] -eq 0 -and $bytes[2] -eq 254 -and $bytes[3] -eq 255) { $offset = 4 }
                elseif ($bytes.Length -ge 4 -and $bytes[0] -eq 255 -and $bytes[1] -eq 254 -and $bytes[2] -eq 0 -and $bytes[3] -eq 0) { throw 'file encoding utf-32be conflicts with a UTF-32LE BOM.' }
            }
        }
        return $selected.encoding.GetString($bytes, $offset, $bytes.Length - $offset)
    }
    'fs.write_text' {
        $selected = & $__rukbat_text_encoding ([string]$values[2]) 'file'
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

    private const string JsonValueRuntime = """
    'json.get' { return $values[0].Get([string]$values[1]) }
    'json.has' { return $values[0].Has([string]$values[1]) }
    'json.items' { return ,$values[0].Items() }
    'json.keys' { return ,$values[0].Keys() }
    'json.kind' { return $values[0].Kind }
    'json.set' { return $values[0].Set([string]$values[1], $values[2]) }
    'json.remove' { return $values[0].Remove([string]$values[1]) }
    'json.set_at' { return $values[0].SetAt([int]$values[1], $values[2]) }
    'json.remove_at' { return $values[0].RemoveAt([int]$values[1]) }
    'json.append' { return $values[0].Append($values[1]) }
    """;

    private const string CsvRuntime = """
    'csv.parse' { return ,(& $__rukbat_csv_parse ([string]$values[0])) }
    'csv.stringify' { return (& $__rukbat_csv_stringify $values[0]) }
    'csv.parse_table' {
        $records = & $__rukbat_csv_parse ([string]$values[0])
        if ($records.Count -eq 0) { throw 'CSV table requires a header record.' }
        $columns = [string[]]$records[0]
        $data = [System.Collections.Generic.List[object]]::new()
        for ($index = 1; $index -lt $records.Count; $index++) { $data.Add($records[$index]) }
        $rows = [RukbatCsvRow]::CreateRows($columns, [object[]]$data, 2)
        return ,@((,$columns); (,$rows))
    }
    'csv.table' {
        $columns = [string[]]$values[0]
        $rows = [RukbatCsvRow]::CreateRows($columns, [object[]]$values[1], 1)
        return ,@((,$columns); (,$rows))
    }
    'csv.row' { return [RukbatCsvRow]::Create([string[]]$values[0], [string[]]$values[1]) }
    'csv.get' { return $values[0].Get([string]$values[1]) }
    'csv.has_column' { return [RukbatCsvRow]::HasColumn([string[]]$values[0], [string]$values[1]) }
    'csv.set' { return $values[0].Set([string]$values[1], [string]$values[2]) }
    'csv.stringify_table' {
        $columns = [string[]]$values[0]
        [RukbatCsvRow]::ValidateColumns($columns)
        $records = [System.Collections.Generic.List[object]]::new()
        $records.Add($columns)
        foreach ($row in $values[1]) { $records.Add($row.Fields($columns)) }
        return (& $__rukbat_csv_stringify $records)
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
        $selected = & $__rukbat_text_encoding ([string]$values[3]) 'process'
        $timeout = $values[2]
        if ($null -ne $timeout -and [long]$timeout -le 0) {
            throw 'process timeout must be greater than zero.'
        }
        if ($null -ne $timeout -and [long]$timeout -gt [int]::MaxValue) {
            throw 'process timeout must not exceed 2147483647 milliseconds.'
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
        $result = [RukbatProcessJob]::RunBytes($start.FileName, [string]::Join(' ', $quoted), $__rukbat_working_dir, $timeout)
        $stdout = $selected.encoding.GetString([byte[]]$result[1])
        $stderr = $selected.encoding.GetString([byte[]]$result[2])
        return ,([object[]]@($result[0], [string]$stdout, [string]$stderr))
    }
    """;
}
