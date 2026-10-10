# Capture the entry script's filesystem location, never a later PowerShell location.
$__rukbat_reject_invalid_path_characters = {
    param([string]$value)
    $hasControlCharacter = @($value.ToCharArray() | Where-Object { [int][char]$_ -lt 32 }).Count -ne 0
    if ($value.IndexOfAny([char[]]'"<>|?*') -ge 0 -or $hasControlCharacter) {
        throw 'Path contains invalid characters.'
    }
}.GetNewClosure()
$__rukbat_resolve_path = {
    param([string]$value, [string]$baseDirectory = $__rukbat_working_dir)
    if ([string]::IsNullOrWhiteSpace($value)) { throw 'Path must not be empty or whitespace.' }
    & $__rukbat_reject_invalid_path_characters $value
    if ($value -match '^[A-Za-z]:(?![\\/])') {
        throw 'Drive-relative paths are not supported; use an absolute or working-directory-relative path.'
    }
    if ($value -match '^[\\/](?![\\/])') {
        $value = [IO.Path]::Combine([IO.Path]::GetPathRoot($baseDirectory), $value.Substring(1))
    } elseif (-not [IO.Path]::IsPathRooted($value)) {
        $value = [IO.Path]::Combine($baseDirectory, $value)
    }
    return [IO.Path]::GetFullPath($value)
}.GetNewClosure()
$__rukbat_entry_attributes = {
    param([string]$path)
    try { return [System.IO.File]::GetAttributes($path) }
    catch [System.IO.FileNotFoundException] { return $null }
    catch [System.IO.DirectoryNotFoundException] { return $null }
}.GetNewClosure()
$__rukbat_reject_directory_mutation = {
    param([string]$path, [string]$operation = 'delete')
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
        throw "Refusing to $operation a filesystem root."
    }
    $working = & $trim ([System.IO.Path]::GetFullPath($__rukbat_working_dir))
    if ([string]::Equals($target, $working, [System.StringComparison]::OrdinalIgnoreCase) -or
        $working.StartsWith($target + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to $operation the captured working directory or one of its ancestors."
    }
}.GetNewClosure()
