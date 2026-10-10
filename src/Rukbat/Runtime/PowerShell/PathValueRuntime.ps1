# Classify ordinary Windows path values without consulting filesystem entries.
# Do not rely on IsPathRooted: a root-relative path has no fixed drive/share.
$__rukbat_path_parts = {
    param([string]$value)
    if ([string]::IsNullOrWhiteSpace($value)) { throw 'Path must not be empty or whitespace.' }
    & $__rukbat_reject_invalid_path_characters $value
    $normalised = $value.Replace('/', '\')
    $root = ''
    $absolute = $false
    if ($normalised -match '^[A-Za-z]:') {
        if ($normalised -notmatch '^[A-Za-z]:\\') { throw 'Drive-relative paths are not supported.' }
        $root = $normalised.Substring(0, 3)
        $absolute = $true
        if ($normalised.Substring(2).Contains(':')) { throw 'Alternate data streams are not supported.' }
    } else {
        if ($normalised.Contains(':')) { throw 'Provider paths and alternate data streams are not supported.' }
        if ($normalised.StartsWith('\\')) {
            if ($normalised -notmatch '^\\\\([^\\]+)\\([^\\]+)(?:\\|$)') { throw 'A UNC path requires a server and share.' }
            $server = $Matches[1]
            $share = $Matches[2]
            if ([string]::IsNullOrWhiteSpace($server) -or [string]::IsNullOrWhiteSpace($share) -or
                $server -eq '.' -or $server -eq '..' -or $share -eq '.' -or $share -eq '..') {
                throw 'Device namespaces and invalid UNC roots are not supported.'
            }
            $root = '\\' + $server + '\' + $share + '\'
            $absolute = $true
        } elseif ($normalised.StartsWith('\')) {
            $root = '\'
        }
    }
    return [pscustomobject]@{ value = $normalised; root = $root; absolute = $absolute }
}.GetNewClosure()
$__rukbat_path_absolute = {
    param([string]$value)
    $parts = & $__rukbat_path_parts $value
    if (-not $parts.absolute) { throw 'A fully qualified absolute path is required.' }
    $full = & $__rukbat_resolve_path $parts.value
    $resolved = & $__rukbat_path_parts $full
    if (-not $resolved.absolute) { throw 'A fully qualified absolute path is required.' }
    # Framework and modern .NET differ on a UNC root's trailing separator.
    if ([string]::Equals($resolved.value.TrimEnd([char]'\'), $resolved.root.TrimEnd([char]'\'), [StringComparison]::OrdinalIgnoreCase)) {
        return $resolved.root
    }
    return $resolved.value
}.GetNewClosure()
$__rukbat_path_filename = {
    param([string]$value)
    $parts = & $__rukbat_path_parts $value
    if ($parts.value.EndsWith('\') -or ($parts.absolute -and
        [string]::Equals($parts.value, $parts.root.TrimEnd([char]'\'), [StringComparison]::OrdinalIgnoreCase))) {
        return ''
    }
    $name = $parts.value.Substring($parts.value.LastIndexOf('\') + 1)
    if ($name -eq '.' -or $name -eq '..') { return '' }
    return $name
}.GetNewClosure()
