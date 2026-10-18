namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitNativeDefinition(string name, string prefix = "")
    {
        Line(prefix + "Add-Type -TypeDefinition @'");
        using var reader = new StringReader(RuntimeResources.ReadNative(name));
        while (reader.ReadLine() is { } line)
            Line(prefix + line);
        RawLine("'@ | Out-Null");
    }

    private void EmitNativeLoader(string name, string? assemblyName = null)
    {
        Line($"$__rukbat_load_{name} = {{");
        Line($"if ($null -eq ('Rukbat{name}' -as [type])) {{");
        if (assemblyName is not null)
            Line($"Add-Type -AssemblyName {assemblyName}");
        Line(assemblyName is null ? "Add-Type -TypeDefinition @'" : "$source = @'");
        RawLine(RuntimeResources.ReadNative(name));
        if (assemblyName is null)
        {
            RawLine("'@ | Out-Null");
        }
        else
        {
            RawLine("'@");
            Line("if ($PSVersionTable.PSEdition -ceq 'Desktop') {");
            Line($"Add-Type -TypeDefinition $source -ReferencedAssemblies @('System.dll', 'System.Core.dll', '{assemblyName}.dll') | Out-Null");
            Line("} else { Add-Type -TypeDefinition $source | Out-Null }");
        }
        Line("}");
        Line("}.GetNewClosure()");
    }
}
