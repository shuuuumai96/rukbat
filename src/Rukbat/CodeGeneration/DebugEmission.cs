namespace Rukbat
{
    // Development-only metadata. Deployment builds contain neither absolute source
    // paths nor debugger hooks. Types and storage names come from the checked program.
    public sealed class DebugMap
    {
        public int Version { get; } = 1;
        public List<DebugPoint> Points { get; } = [];
        public Dictionary<string, DebugValueType> Types { get; } = [];
    }

    public sealed record DebugPoint(int Id, string File, int Line, int Column,
        int GeneratedLine, string Function, IReadOnlyList<DebugVariable> Variables);
    public sealed record DebugVariable(string Name, string Storage, bool Global, string Type, bool IsConst);
    public sealed record DebugValueType(string Name, string? Element = null,
        string? Nullable = null, IReadOnlyList<DebugField>? Fields = null,
        string? DictionaryKey = null, string? DictionaryValue = null,
        IReadOnlyDictionary<string, string>? DisplayValues = null);
    public sealed record DebugField(string Name, string Storage, string Type);

    internal sealed partial class Emitter
    {
        private List<VariableSymbol> debugVariables = [];
        private string debugFunction = "<module>";
        private readonly Dictionary<ValueType, string> debugTypes = [];

        private string DebugType(ValueType type)
        {
            if (debugTypes.TryGetValue(type, out var existing))
            {
                return existing;
            }

            var id = "t" + debugTypes.Count;
            debugTypes.Add(type, id);
            // Register before walking fields, so recursive nullable structs terminate.
            debugMap!.Types[id] = new(type.Name);
            debugMap.Types[id] = new(type.Name,
                type.Element is { } element ? DebugType(element) : null,
                type.NullableElement is { } nullable ? DebugType(nullable) : null,
                type.Struct is { IsCsvRow: false, IsJsonValue: false, IsCliResult: false } structure
                    ? structure.Fields.Select(pair => new DebugField(pair.Key, pair.Value.Key, DebugType(pair.Value.Type))).ToArray()
                    : null,
                type.DictionaryKey is { } key ? DebugType(key) : null,
                type.DictionaryValue is { } value ? DebugType(value) : null,
                type == LogValues.Level ? LogValues.Levels.ToDictionary(value => value.Tag, value => "log." + value.Name, StringComparer.Ordinal) : null);
            return id;
        }

        private void DebugEnter(string name, IEnumerable<VariableSymbol> variables)
        {
            if (debugMap is null)
            {
                return;
            }

            debugFunction = name;
            debugVariables = variables.ToList();
            Line("$null = $global:__rukbat_debug_frames.Add(@{ point = -1; variables = @{} })");
            Line("try {");
            indentation++;
        }

        private void DebugLeave()
        {
            if (debugMap is null)
            {
                return;
            }

            indentation--;
            Line("} finally {");
            Line("    $global:__rukbat_debug_frames.RemoveAt($global:__rukbat_debug_frames.Count - 1)");
            Line("}");
        }

        private void DebugPoint(Statement statement)
        {
            if (debugMap is null)
            {
                return;
            }

            var prefix = module.Source.Text[..statement.Token.Offset];
            var variables = debugVariables.DistinctBy(symbol => symbol.Name).Select(symbol => new DebugVariable(
                symbol.Name, symbol.PowerShellName.Split(':').Last().TrimStart('$'), symbol.IsGlobal,
                DebugType(symbol.Type), symbol.IsConst)).ToArray();
            var id = debugMap.Points.Count;
            // Keep PSVariable references, rather than copies, for suspended callers and
            // module globals. Get-PSCallStack.GetFrameVariables omits them in PS 5.1.
            var captures = variables.Select(variable => $"{Quote(variable.Storage)} = " +
                $"(Get-Variable -Name {Quote(variable.Storage)} -Scope {(variable.Global ? "Script" : "Local")} -ErrorAction SilentlyContinue)");
            Line($"$global:__rukbat_debug_frames[-1].point = {id}");
            Line("$global:__rukbat_debug_frames[-1].variables = @{" + string.Join("; ", captures) + "}");
            debugMap.Points.Add(new(id, module.Source.Path, prefix.Count(character => character == '\n') + 1,
                prefix.Length - prefix.LastIndexOf('\n'), generatedLine, debugFunction, variables));
        }
    }
}
