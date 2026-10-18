namespace Rukbat;

internal sealed record SourceComment(int Start, int End, string Text)
{
    public bool IsModule => Text.StartsWith("//!", StringComparison.Ordinal);
    public bool IsDeclaration => Text.StartsWith("///", StringComparison.Ordinal) && !Text.StartsWith("////", StringComparison.Ordinal);
    public bool IsDocumentation => IsModule || IsDeclaration;
    public bool IsStandalone(Source source)
    {
        var lineStart = source.Text.LastIndexOf('\n', Math.Max(0, Start - 1)) + 1;
        return source.Text[lineStart..Start].All(c => c is ' ' or '\t' or '\r');
    }
}

internal sealed record DocumentationLine(string Text, int Offset);

// Keep the original source map even after removing the comment prefix. Markdown
// and checked examples use this map to report positions in the author's file.
internal sealed class DocumentationText(Source source, IReadOnlyList<SourceComment> comments)
{
    public Source Source { get; } = source;
    public int Start { get; } = comments[0].Start;
    public int End { get; } = comments[^1].End;
    public IReadOnlyList<DocumentationLine> Lines { get; } = comments.Select(comment =>
    {
        const int prefixLength = 3;
        var prefix = prefixLength + (comment.Text.Length > prefixLength && comment.Text[prefixLength] == ' ' ? 1 : 0);
        return new DocumentationLine(comment.Text[prefix..], comment.Start + prefix);
    }).ToArray();
    public string Markdown => string.Join("\n", Lines.Select(line => line.Text));
    public string Summary => string.Join(" ", Lines.TakeWhile(line => !string.IsNullOrWhiteSpace(line.Text)).Select(line => line.Text));
    public int SourceOffset(int markdownOffset)
    {
        foreach (var line in Lines)
        {
            if (markdownOffset <= line.Text.Length)
                return line.Offset + Math.Max(0, markdownOffset);
            markdownOffset -= line.Text.Length + 1;
        }
        return End;
    }
    public Diagnostic Error(string code, string message, int markdownOffset = 0)
    {
        var position = new EditorText(Source.Text).Position(SourceOffset(markdownOffset));
        return new(code, message, position.Line + 1, position.Character + 1, Source.Path);
    }
}

internal sealed record DocumentationDeclaration(Node Node, int Start, string Name, string Kind, StructDeclaration? Owner = null);

internal sealed class ModuleDocumentation
{
    public DocumentationText? Overview { get; private set; }
    public List<DocumentationDeclaration> Declarations { get; } = [];
    public Dictionary<Node, DocumentationText> Comments { get; } = new(ReferenceEqualityComparer.Instance);
    public List<Diagnostic> Diagnostics { get; } = [];
    public string Text(Node node) => Comments.GetValueOrDefault(node)?.Markdown ?? "";
    public IEnumerable<DocumentationText> Texts
    {
        get
        {
            if (Overview is { } overview)
                yield return overview;
            foreach (var text in Comments.Values)
                yield return text;
        }
    }

    public ModuleDocumentation(Module module)
    {
        var source = module.Source;
        foreach (var statement in module.Statements)
        {
            if (statement is Function function)
                Add(function, function.Name, "function");
            else if (statement is Variable variable)
                Add(variable, variable.Token.Text, "variable");
            else if (statement is StructDeclaration structure)
            {
                Add(structure, structure.Token.Text, "struct");
                foreach (var field in structure.Fields)
                    Add(field, structure.Token.Text + "." + field.Token.Text, "field", structure);
                foreach (var method in structure.Methods)
                    Add(method, structure.Token.Text + "." + method.Name, "method", structure);
            }
        }
        Declarations.Sort((left, right) => left.Start.CompareTo(right.Start));

        var comments = source.Comments;
        for (var index = 0; index < comments.Count; index++)
        {
            var first = comments[index];
            if (!first.IsDocumentation)
                continue;
            var group = new List<SourceComment> { first };
            while (index + 1 < comments.Count && comments[index + 1].IsDocumentation &&
                comments[index + 1].IsModule == first.IsModule &&
                comments[index + 1].IsStandalone(source) && Adjacent(source, comments[index].End, comments[index + 1].Start))
                group.Add(comments[++index]);
            var text = new DocumentationText(source, group);
            if (!first.IsStandalone(source))
                Diagnostics.Add(text.Error("RUK6000", "A documentation comment must occupy its own line."));
            else if (first.IsModule)
            {
                if (first.Start >= source.FirstCodeOffset || Overview is not null)
                    Diagnostics.Add(text.Error("RUK6000", "Place one module documentation block before the first code statement."));
                else
                    Overview = text;
            }
            else
            {
                var declaration = Declarations.FirstOrDefault(d => d.Start >= text.End && Adjacent(source, text.End, d.Start));
                if (declaration is null)
                    Diagnostics.Add(text.Error("RUK6000", "Place documentation immediately before a top-level declaration, struct field, or method."));
                else
                    Comments.Add(declaration.Node, text);
            }
        }

        void Add(Node node, string name, string kind, StructDeclaration? owner = null)
        {
            if (source.DeclarationStarts.TryGetValue(node, out var start))
                Declarations.Add(new(node, start, name, kind, owner));
        }
    }

    private static bool Adjacent(Source source, int end, int start)
    {
        var gap = source.Text[end..start];
        return gap.Count(c => c == '\n') == 1 && gap.All(char.IsWhiteSpace);
    }
}
