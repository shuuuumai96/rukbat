namespace Rukbat
{
    internal sealed class Source(string text, string path)
    {
        public string Text { get; } = text;
        public string Path { get; } = path;
        public List<Diagnostic> Diagnostics { get; } = [];
        public List<SourceComment> Comments { get; } = [];
        public int FirstCodeOffset { get; set; } = text.Length;
        public Dictionary<Node, int> DeclarationStarts { get; } = new(ReferenceEqualityComparer.Instance);
        public void Error(string code, string message, Token token)
        {
            var prefix = Text[..Math.Clamp(token.Offset, 0, Text.Length)];
            var line = prefix.Count(character => character == '\n') + 1;
            var column = prefix.Length - prefix.LastIndexOf('\n');
            Diagnostics.Add(new(code, message, line, column, Path));
        }
    }
}
