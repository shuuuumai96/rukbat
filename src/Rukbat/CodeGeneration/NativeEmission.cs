namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private const string UsingPrefix = "using ";

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

        private void EmitNativeLoader(string name, string? typeName = null) => EmitNativeLoader(name, typeName, () =>
        {
            Line("Add-Type -TypeDefinition @'");
            RawLine(RuntimeResources.ReadNative(name));
            RawLine("'@ | Out-Null");
        });

        private void EmitNativeLoader(string name, string assemblyName, string? typeName = null) => EmitNativeLoader(name, typeName, () =>
        {
            Line($"Add-Type -AssemblyName {assemblyName}");
            Line("$source = @'");
            RawLine(RuntimeResources.ReadNative(name));
            RawLine("'@");
            Line("if ($PSVersionTable.PSEdition -ceq 'Desktop') {");
            Line($"Add-Type -TypeDefinition $source -ReferencedAssemblies @('System.dll', 'System.Core.dll', '{assemblyName}.dll') | Out-Null");
            Line("} else { Add-Type -TypeDefinition $source | Out-Null }");
        });

        // Compile all active core C# helpers in a single Add-Type invocation on first use,
        // avoiding multiple csc.exe / Roslyn startups when a script combines several modules.
        private void EmitBundledNativeLoader(IReadOnlyList<(string Name, string ProbeType)> units)
        {
            if (units.Count == 0)
            {
                return;
            }

            var bundledSource = BundleNativeSources(units.Select(unit => unit.Name));
            EmitNativeLoader(RuntimeCatalog.Native.CoreBundle, units[0].ProbeType, () =>
            {
                Line("Add-Type -TypeDefinition @'");
                RawLine(bundledSource);
                RawLine("'@ | Out-Null");
            });

            foreach (var (name, _) in units)
            {
                Line($"$__rukbat_load_{name} = $__rukbat_load_{RuntimeCatalog.Native.CoreBundle}");
            }
        }

        private static string BundleNativeSources(IEnumerable<string> names)
        {
            var usings = new List<string>();
            var seenUsings = new HashSet<string>(StringComparer.Ordinal);
            var bodies = new List<string>();

            foreach (var name in names)
            {
                using var reader = new StringReader(RuntimeResources.ReadNative(name));
                while (reader.ReadLine() is { } line)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith(UsingPrefix, StringComparison.Ordinal) && trimmed.EndsWith(';'))
                    {
                        if (seenUsings.Add(trimmed))
                        {
                            usings.Add(trimmed);
                        }
                    }
                    else
                    {
                        bodies.Add(line);
                    }
                }
            }

            return string.Join('\n', usings.Concat(bodies));
        }

        private void EmitNativeLoader(string name, string? typeName, Action emit)
        {
            Line($"$__rukbat_load_{name} = {{");
            Line($"if ($null -eq ('{typeName ?? "Rukbat" + name}' -as [type])) {{");
            emit();
            Line("}");
            Line("}.GetNewClosure()");
        }
    }
}
