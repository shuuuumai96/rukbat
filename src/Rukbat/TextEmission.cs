namespace Rukbat;

internal sealed partial class Emitter
{
    private const string TextRuntime = """
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
