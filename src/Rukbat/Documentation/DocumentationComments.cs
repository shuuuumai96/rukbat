namespace Rukbat
{
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
                {
                    return line.Offset + Math.Max(0, markdownOffset);
                }

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

    internal sealed record DocumentationDeclaration(Node Node, int Start, string Name, string Kind);

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
                {
                    yield return overview;
                }

                foreach (var text in Comments.Values)
                {
                    yield return text;
                }
            }
        }

        private readonly Source source;

        public ModuleDocumentation(Module module)
        {
            source = module.Source;
            foreach (var statement in module.Statements)
            {
                DeclarationReaders.DispatchIfRegistered(this, statement);
            }
            Declarations.Sort((left, right) => left.Start.CompareTo(right.Start));

            var comments = source.Comments;
            for (var index = 0; index < comments.Count; index++)
            {
                var first = comments[index];
                if (!first.IsDocumentation)
                {
                    continue;
                }

                var group = new List<SourceComment> { first };
                while (index + 1 < comments.Count && comments[index + 1].IsDocumentation &&
                    comments[index + 1].IsModule == first.IsModule &&
                    comments[index + 1].IsStandalone(source) && Adjacent(source, comments[index].End, comments[index + 1].Start))
                {
                    group.Add(comments[++index]);
                }

                var text = new DocumentationText(source, group);
                if (!first.IsStandalone(source))
                {
                    Diagnostics.Add(text.Error(DiagnosticCode.InvalidDocumentation, "A documentation comment must occupy its own line."));
                }
                else if (first.IsModule)
                {
                    if (first.Start >= source.FirstCodeOffset || Overview is not null)
                    {
                        Diagnostics.Add(text.Error(DiagnosticCode.InvalidDocumentation, "Place one module documentation block before the first code statement."));
                    }
                    else
                    {
                        Overview = text;
                    }
                }
                else
                {
                    var declaration = Declarations.FirstOrDefault(d => d.Start >= text.End && Adjacent(source, text.End, d.Start));
                    if (declaration is null)
                    {
                        Diagnostics.Add(text.Error(DiagnosticCode.InvalidDocumentation, "Place documentation immediately before a top-level declaration, struct field, method, or enum member."));
                    }
                    else
                    {
                        Comments.Add(declaration.Node, text);
                    }
                }
            }
        }

        private static readonly NodeHandlers<ModuleDocumentation> DeclarationReaders = new(
            NodeHandlers<ModuleDocumentation>.For<Function>(static (documentation, node) => documentation.Add(node, node.Name, "function")),
            NodeHandlers<ModuleDocumentation>.For<Variable>(static (documentation, node) => documentation.Add(node, node.Token.Text, "variable")),
            NodeHandlers<ModuleDocumentation>.For<StructDeclaration>(static (documentation, node) => documentation.ReadStruct(node)),
            NodeHandlers<ModuleDocumentation>.For<EnumDeclaration>(static (documentation, node) => documentation.ReadEnum(node)),
            NodeHandlers<ModuleDocumentation>.For<ImplDeclaration>(static (documentation, node) => documentation.ReadImpl(node)));

        private void Add(Node node, string name, string kind)
        {
            if (source.DeclarationStarts.TryGetValue(node, out var start))
            {
                Declarations.Add(new(node, start, name, kind));
            }
        }

        private void ReadStruct(StructDeclaration structure)
        {
            Add(structure, structure.Token.Text, "struct");
            foreach (var field in structure.Fields)
            {
                Add(field, structure.Token.Text + "." + field.Token.Text, "field");
            }
        }

        private void ReadEnum(EnumDeclaration enumeration)
        {
            Add(enumeration, enumeration.Token.Text, "enum");
            foreach (var member in enumeration.Members)
            {
                Add(member, enumeration.Token.Text + "." + member.Token.Text, "enum-member");
            }
        }

        private void ReadImpl(ImplDeclaration implementation)
        {
            foreach (var method in implementation.Methods)
            {
                Add(method, implementation.Token.Text + "." + method.Name, "method");
            }
        }

        private static bool Adjacent(Source source, int end, int start)
        {
            var gap = source.Text[end..start];
            return gap.Count(c => c == '\n') == 1 && gap.All(char.IsWhiteSpace);
        }
    }
}
