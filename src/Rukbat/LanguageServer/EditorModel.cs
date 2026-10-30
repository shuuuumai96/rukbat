namespace Rukbat
{
    internal sealed record EditorPosition(int Line, int Character);
    internal sealed record EditorRange(EditorPosition Start, EditorPosition End);
    internal sealed record EditorLocation(string Uri, EditorRange Range);

    // LSP uses UTF-16 code units, as do .NET strings and the compiler's token offsets.
    internal sealed class EditorText(string text)
    {
        private readonly int[] lines = [0, .. text.Select((c, i) => (c, i)).Where(p => p.c == '\n').Select(p => p.i + 1)];
        public int Offset(EditorPosition position)
        {
            if (position.Line < 0 || position.Line >= lines.Length || position.Character < 0)
            {
                throw new ArgumentException("Position is outside the document.");
            }

            var end = position.Line + 1 < lines.Length ? lines[position.Line + 1] - 1 : text.Length;
            if (end > lines[position.Line] && text[end - 1] == '\r')
            {
                end--;
            }

            if (position.Character > end - lines[position.Line])
            {
                throw new ArgumentException("Position is outside the line.");
            }

            return lines[position.Line] + position.Character;
        }
        public EditorPosition Position(int offset)
        {
            offset = Math.Clamp(offset, 0, text.Length);
            var line = Array.BinarySearch(lines, offset);
            if (line < 0)
            {
                line = ~line - 1;
            }

            return new(line, offset - lines[line]);
        }
        public EditorRange Range(int start, int length) => new(Position(start), Position(start + length));
    }

    internal sealed record EditorSymbol(string Name, string Detail, int Kind, string Documentation = "",
        Module? Module = null, Token? Declaration = null, ValueType? Type = null, bool IsFixed = false, bool IsModuleVariable = false)
    {
        public int AvailableFrom { get; init; } = Declaration?.Offset ?? 0;
        public EditorSymbolId Identity => Module is not null && Declaration is not null
            ? new(EditorPaths.Key(Module.Source.Path), Declaration.Offset, Kind)
            : new("<builtin:" + Detail.Split('(', '<', ':')[0].Trim() + ">", 0, Kind);
        public bool CanRename => !IsFixed && Module is not null && Declaration is not null && !Module.Source.Path.StartsWith('<') && Name != "self";
        public object Completion => new
        {
            label = Name,
            kind = Kind,
            detail = Detail,
            documentation = new
            {
                kind = "markdown",
                value = $"```rukbat\n{Detail}\n```" + (Documentation.Length == 0 ? "" : "\n\n" + Documentation.Split("\n\n")[0])
            }
        };
        public string Markdown => $"```rukbat\n{Detail}\n```" + (Documentation.Length == 0 ? "" : "\n\n" + Documentation);
        public EditorLocation? Location => Module is null || Declaration is null || Module.Source.Path.StartsWith('<') ? null
            : new(new Uri(Module.Source.Path).AbsoluteUri, new EditorText(Module.Source.Text).Range(Declaration.Offset, Declaration.Text.Length));
    }
}
