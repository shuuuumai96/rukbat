namespace Rukbat
{
    internal sealed class Module(Source source, string id, List<Statement> statements, string? standardName = null)
    {
        private ModuleDocumentation? documentation;
        public ModuleDocumentation Documentation => documentation ??= new(this);
        public Source Source { get; } = source;
        public string Id { get; } = id;
        public string? StandardName { get; } = standardName;
        public List<Statement> Statements { get; } = statements;
        public Dictionary<List<Statement>, (int Start, int End)>? Blocks { get; set; }
        public Dictionary<ValueType, Token>? TypeTokens { get; set; }
        public Dictionary<ValueType, Token>? TypeQualifiers { get; set; }
        public Dictionary<List<Argument>, CallSyntax>? CallSites { get; set; }
        public Dictionary<string, Module> Imports { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, StructSymbol> Structs { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Function> Functions { get; } = new(StringComparer.Ordinal);
    }
}
