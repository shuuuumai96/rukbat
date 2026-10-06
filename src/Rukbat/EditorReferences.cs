using Markdig.Syntax;

namespace Rukbat;

internal static class EditorPaths
{
    public static readonly StringComparer Comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    public static string Key(string path) => OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
    public static bool Contains(string root, string path) => Comparer.Equals(root, path) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}

internal sealed record EditorSymbolId(string Path, int Offset, int Kind);
internal sealed record EditorOccurrence(Module Module, Token Token, EditorSymbol Symbol, bool Searchable = true)
{
    public EditorSymbolId Identity => Symbol.Identity;
    public bool IsDeclaration => EditorPaths.Comparer.Equals(Module.Source.Path, Symbol.Module?.Source.Path) && Token.Offset == Symbol.Declaration?.Offset;
    public EditorLocation Location => new(new Uri(Module.Source.Path).AbsoluteUri,
        new EditorText(Module.Source.Text).Range(Token.Offset, Token.Text.Length));
}

internal sealed partial class LanguageAnalysis
{
    private void AddCheckedExamples(Func<string, string> readSource)
    {
        foreach (var module in Modules.Where(m => m.StandardName is null))
        {
            var texts = module.Documentation.Comments.Values.AsEnumerable();
            if (module.Documentation.Overview is { } overview)
                texts = texts.Prepend(overview);
            var index = 0;
            foreach (var text in texts)
            {
                foreach (var code in DocumentationMarkdown.Parse(text).Descendants<FencedCodeBlock>().Where(DocumentationMarkdown.IsChecked))
                {
                    var lines = code.Lines.Lines.Take(code.Lines.Count).ToArray();
                    var snippet = string.Join("\n", lines.Select(line => line.ToString()));
                    var virtualPath = Path.Combine(Path.GetDirectoryName(module.Source.Path)!,
                        $".rukbat-editor-example-{Path.GetFileName(module.Source.Path)}-{index++}.rukbat");
                    var analysis = new LanguageAnalysis(snippet, virtualPath, readSource, includeExamples: false);
                    ReferencedPaths.UnionWith(analysis.ReferencedPaths.Where(p => !EditorPaths.Comparer.Equals(p, virtualPath)));
                    var map = new EditorText(snippet);
                    int OriginalOffset(int offset)
                    {
                        var position = map.Position(offset);
                        return lines.Length == 0 ? text.Start : text.SourceOffset(lines[Math.Min(position.Line, lines.Length - 1)].Slice.Start + position.Character);
                    }
                    foreach (var diagnostic in analysis.Diagnostics)
                    {
                        if (!EditorPaths.Comparer.Equals(diagnostic.FilePath, virtualPath))
                            ExampleDiagnostics.Add(diagnostic);
                        else
                        {
                            var position = new EditorText(module.Source.Text).Position(OriginalOffset(map.Offset(new(diagnostic.Line - 1, diagnostic.Column - 1))));
                            ExampleDiagnostics.Add(diagnostic with
                            {
                                FilePath = module.Source.Path,
                                Line = position.Line + 1,
                                Column = position.Character + 1
                            });
                        }
                    }
                    foreach (var occurrence in analysis.Occurrences.Where(o => EditorPaths.Comparer.Equals(o.Module.Source.Path, virtualPath)))
                    {
                        var symbol = occurrence.Symbol;
                        if (EditorPaths.Comparer.Equals(symbol.Module?.Source.Path, virtualPath) && symbol.Declaration is { } declaration)
                            symbol = symbol with
                            {
                                Module = module,
                                Declaration = declaration with
                                {
                                    Offset = OriginalOffset(declaration.Offset)
                                }
                            };
                        occurrences.Add(occurrence with
                        {
                            Module = module,
                            Token = occurrence.Token with
                            {
                                Offset = OriginalOffset(occurrence.Token.Offset)
                            },
                            Symbol = symbol
                        });
                    }
                }
            }
        }
    }

    private void AddDocumentationReferences()
    {
        foreach (var module in Modules.Where(m => m.StandardName is null))
        {
            var texts = module.Documentation.Comments.Values.AsEnumerable();
            if (module.Documentation.Overview is { } overview)
                texts = texts.Prepend(overview);
            foreach (var text in texts)
            {
                foreach (var reference in DocumentationMarkdown.Parse(text).Descendants<DocumentationReference>())
                {
                    var target = documentation.Resolve(module, reference.Name);
                    if (target is null)
                        continue;
                    var start = reference.Span.Start;
                    var nameStart = text.Markdown.IndexOf(reference.Name, start, StringComparison.Ordinal);
                    if (nameStart < 0 || nameStart > reference.Span.End)
                        continue;
                    var parts = reference.Name.Split('.');
                    var partOffset = nameStart;
                    var owner = module;
                    if (parts.Length > 1 && module.Imports.TryGetValue(parts[0], out var imported))
                    {
                        var alias = scopes.First(s => s.Module == module && s.Parent is null).Symbols.First(s => s.Kind == 9 && s.Name == parts[0]);
                        Add(module, new(parts[0], text.SourceOffset(partOffset)), alias);
                        owner = imported;
                        partOffset += parts[0].Length + 1;
                        parts = parts[1..];
                    }
                    if (parts.Length > 1 && owner.Structs.TryGetValue(parts[0], out var structure))
                    {
                        Add(module, new(parts[0], text.SourceOffset(partOffset)), symbols[structure]);
                        partOffset += parts[0].Length + 1;
                    }
                    var declaration = occurrences.FirstOrDefault(o => o.IsDeclaration &&
                        o.Module == target.Module && o.Token.Offset == target.Declaration.Node.Token.Offset);
                    if (declaration is not null)
                        Add(module, new(parts[^1], text.SourceOffset(partOffset)), declaration.Symbol);
                }
            }
        }
    }
}
