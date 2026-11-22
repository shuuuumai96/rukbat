namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private void EmitNativeDefinition(string name, string prefix = "")
        {
            Line(prefix + "Add-Type -TypeDefinition @'");
            using var reader = new StringReader(RuntimeResources.ReadNative(name));
            while (reader.ReadLine() is { } line)
            {
                Line(prefix + line);
            }

            RawLine("'@ | Out-Null");
        }

        private void EmitNativeLoader(string name) => EmitNativeLoader(name, () =>
        {
            Line("Add-Type -TypeDefinition @'");
            RawLine(RuntimeResources.ReadNative(name));
            RawLine("'@ | Out-Null");
        });

        private void EmitNativeLoader(string name, string assemblyName) => EmitNativeLoader(name, () =>
        {
            Line($"Add-Type -AssemblyName {assemblyName}");
            Line("$source = @'");
            RawLine(RuntimeResources.ReadNative(name));
            RawLine("'@");
            Line("if ($PSVersionTable.PSEdition -ceq 'Desktop') {");
            Line($"Add-Type -TypeDefinition $source -ReferencedAssemblies @('System.dll', 'System.Core.dll', '{assemblyName}.dll') | Out-Null");
            Line("} else { Add-Type -TypeDefinition $source | Out-Null }");
        });

        private void EmitNativeLoader(string name, Action emit)
        {
            Line($"$__rukbat_load_{name} = {{");
            Line($"if ($null -eq ('Rukbat{name}' -as [type])) {{");
            emit();
            Line("}");
            Line("}.GetNewClosure()");
        }
    }
}
