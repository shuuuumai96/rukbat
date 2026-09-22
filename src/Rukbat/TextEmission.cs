namespace Rukbat;

internal sealed partial class Emitter
{
    private const string TextRuntime = """
    'text.trim' {
        # Fixed Unicode White_Space set; independent of the host's Unicode tables.
        $value = [string]$values[0]
        $start = 0
        while ($start -lt $value.Length) {
            $character = [int][char]$value[$start]
            if (-not (($character -ge 9 -and $character -le 13) -or $character -eq 32 -or
                $character -eq 133 -or $character -eq 160 -or $character -eq 5760 -or
                ($character -ge 8192 -and $character -le 8202) -or $character -eq 8232 -or
                $character -eq 8233 -or $character -eq 8239 -or $character -eq 8287 -or $character -eq 12288)) {
                break
            }
            $start++
        }
        if ($start -eq $value.Length) { return '' }
        $end = $value.Length - 1
        while ($end -ge $start) {
            $character = [int][char]$value[$end]
            if (-not (($character -ge 9 -and $character -le 13) -or $character -eq 32 -or
                $character -eq 133 -or $character -eq 160 -or $character -eq 5760 -or
                ($character -ge 8192 -and $character -le 8202) -or $character -eq 8232 -or
                $character -eq 8233 -or $character -eq 8239 -or $character -eq 8287 -or $character -eq 12288)) {
                break
            }
            $end--
        }
        if ($start -eq 0 -and $end -eq $value.Length - 1) { return $value }
        return $value.Substring($start, $end - $start + 1)
    }
    'text.contains' {
        return ([string]$values[0]).IndexOf([string]$values[1], [StringComparison]::Ordinal) -ge 0
    }
    'text.starts_with' {
        return ([string]$values[0]).StartsWith([string]$values[1], [StringComparison]::Ordinal)
    }
    'text.ends_with' {
        return ([string]$values[0]).EndsWith([string]$values[1], [StringComparison]::Ordinal)
    }
    'text.replace_all' {
        $search = [string]$values[1]
        if ($search.Length -eq 0) { throw 'text.replace_all: search must not be empty.' }
        return ([string]$values[0]).Replace($search, [string]$values[2])
    }
    'text.split' {
        $separator = [string]$values[1]
        if ($separator.Length -eq 0) { throw 'text.split: separator must not be empty.' }
        return ,([object[]]([string]$values[0]).Split([string[]]@($separator), [StringSplitOptions]::None))
    }
    'text.split_lines' {
        $value = [string]$values[0]
        if ($value.Length -eq 0) { return ,([object[]]@()) }
        $lines = [System.Collections.Generic.List[string]]::new()
        $lines.AddRange([regex]::Split($value, '\r\n|\r|\n'))
        # A final line terminator ends the last line; it does not start another one.
        if ($lines[$lines.Count - 1].Length -eq 0) { $lines.RemoveAt($lines.Count - 1) }
        return ,([object[]]$lines.ToArray())
    }
    'text.join' {
        return [string]::Join([string]$values[1], [string[]]$values[0])
    }
    'text.from_int' {
        return ([int]$values[0]).ToString([Globalization.CultureInfo]::InvariantCulture)
    }
    'text.from_float' {
        return ([double]$values[0]).ToString('R', [Globalization.CultureInfo]::InvariantCulture)
    }
    'text.from_bool' {
        if ([bool]$values[0]) { return 'true' }
        return 'false'
    }
    { $_ -cin @('text.parse_int', 'text.parse_float', 'text.parse_bool') } {
        # ASCII whitespace only: tab through carriage return, and space.
        $text = [regex]::Replace([string]$values[0], '\A[\x09-\x0D\x20]+|[\x09-\x0D\x20]+\z', '')
        $culture = [Globalization.CultureInfo]::InvariantCulture
        if ($operation -ceq 'text.parse_bool') {
            if ($text -ceq 'true') { return $true }
            if ($text -ceq 'false') { return $false }
        } elseif ($operation -ceq 'text.parse_int') {
            $number = 0
            if ($text -cmatch '\A[+-]?[0-9]+\z' -and
                [int]::TryParse($text, [Globalization.NumberStyles]::AllowLeadingSign, $culture, [ref]$number)) {
                return $number
            }
        } else {
            $number = [double]0
            if ($text -cmatch '\A[+-]?[0-9]+(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z' -and
                [double]::TryParse($text, [Globalization.NumberStyles]::Float, $culture, [ref]$number) -and
                -not [double]::IsNaN($number) -and -not [double]::IsInfinity($number)) {
                $significand = ($text -split '[eE]')[0]
                if ($number -ne 0 -or $significand -cnotmatch '[1-9]') { return $number }
            }
        }
        throw "${operation}: invalid format or value outside the supported range."
    }
    """;
}
