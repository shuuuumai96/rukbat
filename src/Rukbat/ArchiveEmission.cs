namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitZipLoader()
    {
        Line("$__rukbat_load_Zip = {");
        Line("if ($null -eq ('RukbatZip' -as [type])) {");
        Line("Add-Type -AssemblyName System.IO.Compression");
        Line("$source = @'");
        using var stream = typeof(Emitter).Assembly.GetManifestResourceStream("Rukbat.Runtime.Zip.cs.txt")!;
        using var reader = new StreamReader(stream);
        RawLine(reader.ReadToEnd());
        RawLine("'@");
        Line("if ($PSVersionTable.PSEdition -ceq 'Desktop') {");
        Line("Add-Type -TypeDefinition $source -ReferencedAssemblies @('System.dll', 'System.Core.dll', 'System.IO.Compression.dll') | Out-Null");
        Line("} else { Add-Type -TypeDefinition $source | Out-Null }");
        Line("}");
        Line("}.GetNewClosure()");
    }

    private const string HashRuntime = """
    'hash.parse' { & $__rukbat_load_Hash; return [RukbatHash]::Parse($values[0]) }
    'hash.name' { return [string]$values[0] }
    'hash.file' {
        & $__rukbat_load_Hash
        return [RukbatHash]::File((& $__rukbat_resolve_path $values[0]), $values[1])
    }
    'hash.text' {
        & $__rukbat_load_Hash
        $selected = & $__rukbat_text_encoding $values[2] 'hash.text'
        return [RukbatHash]::Text($selected.encoding.GetBytes([string]$values[0]), $values[1])
    }
    """;

    private const string ZipRuntime = """
    'zip.create' {
        & $__rukbat_load_Zip
        [RukbatZip]::Create((& $__rukbat_resolve_path $values[0]), (& $__rukbat_resolve_path $values[1]))
        return
    }
    { $_ -cin @('zip.options', 'zip.list', 'zip.extract') } {
        & $__rukbat_load_Zip
        if ($operation -ceq 'zip.options') { $options = @($values[0], [long]100000, [long]10737418240) }
        else { $options = $values[$values.Length - 1] }
        $selected = & $__rukbat_text_encoding $options[0] 'zip names'
        [RukbatZip]::ValidateOptions($selected.name, $options[1], $options[2])
        if ($operation -ceq 'zip.options') { return ,([object[]]$options) }
        $archive = & $__rukbat_resolve_path $values[0]
        if ($operation -ceq 'zip.list') {
            return ,([RukbatZip]::List($archive, $selected.encoding, $options[1], $options[2]))
        }
        [RukbatZip]::Extract($archive, (& $__rukbat_resolve_path $values[1]), $selected.encoding, $options[1], $options[2])
        return
    }
    """;
}
