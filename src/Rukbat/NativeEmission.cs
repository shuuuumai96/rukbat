namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitNativeLoader(string name)
    {
        Line($"$__rukbat_load_{name} = {{");
        Line($"if ($null -eq ('Rukbat{name}' -as [type])) {{");
        Line("Add-Type -TypeDefinition @'");
        using var stream = typeof(Emitter).Assembly.GetManifestResourceStream($"Rukbat.Runtime.{name}.cs.txt")
            ?? throw new InvalidOperationException($"Missing embedded runtime: {name}");
        using var reader = new StreamReader(stream);
        RawLine(reader.ReadToEnd());
        RawLine("'@ | Out-Null");
        Line("}");
        Line("}.GetNewClosure()");
    }
}
