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
}
