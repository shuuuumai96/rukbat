namespace Rukbat;

// One catalog supplies binding, editor members, and the generated encoding selector.
internal sealed record StandardEncoding(string Name, string Label, int CodePage, string Codec, string Documentation);

internal static class EncodingValues
{
    public static readonly IReadOnlyList<StandardEncoding> All =
    [
        new("utf8", "utf-8", 65001, "[System.Text.UTF8Encoding]::new($false, $true)", "UTF-8."),
        new("utf16_le", "utf-16le", 1200, "[System.Text.UnicodeEncoding]::new($false, $false, $true)", "UTF-16 little-endian."),
        new("utf16_be", "utf-16be", 1201, "[System.Text.UnicodeEncoding]::new($true, $false, $true)", "UTF-16 big-endian."),
        new("utf32_le", "utf-32le", 12000, "[System.Text.UTF32Encoding]::new($false, $false, $true)", "UTF-32 little-endian."),
        new("utf32_be", "utf-32be", 12001, "[System.Text.UTF32Encoding]::new($true, $false, $true)", "UTF-32 big-endian."),
        new("ascii", "ascii", 20127, "[System.Text.Encoding]::GetEncoding(20127, [System.Text.EncoderExceptionFallback]::new(), [System.Text.DecoderExceptionFallback]::new())", "Strict ASCII."),
        new("iso_8859_1", "iso-8859-1", 28591, "[System.Text.Encoding]::GetEncoding(28591, [System.Text.EncoderExceptionFallback]::new(), [System.Text.DecoderExceptionFallback]::new())", "ISO-8859-1."),
        Windows(874),
        Windows(1250), Windows(1251), Windows(1252), Windows(1253), Windows(1254),
        Windows(1255), Windows(1256), Windows(1257), Windows(1258),
        Windows(932), Windows(936), Windows(949), Windows(950),
        new("gb18030", "gb18030", 54936, "[RukbatCodePageCodec]::new(54936)", "GB18030 (Windows code page 54936)."),
        Windows(437), Windows(850), Windows(852), Windows(866)
    ];

    private static StandardEncoding Windows(int number) => new($"windows_{number}", $"windows-{number}", number,
        $"[RukbatCodePageCodec]::new({number})", number == 932 ? "Windows CP932, including NEC/IBM extensions." : $"Windows code page {number}.");
}
