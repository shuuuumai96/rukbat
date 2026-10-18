using System.Net;
using System.Text;
using Markdig.Syntax;

namespace Rukbat;

internal sealed record DocumentationEntry(Module Module, DocumentationDeclaration Declaration, DocumentationText? Comment, string Id);

internal sealed class DocumentationCatalog
{
    public List<DocumentationEntry> Entries { get; } = [];
    private readonly Dictionary<Node, DocumentationEntry> entriesByNode = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Module, string> moduleIds = new(ReferenceEqualityComparer.Instance);

    public DocumentationCatalog(IEnumerable<Module> modules, string root)
    {
        foreach (var module in modules.Where(module => module.StandardName is null))
        {
            var path = Path.GetRelativePath(root, module.Source.Path).Replace('\\', '/');
            var moduleId = "module-" + Convert.ToHexString(Encoding.UTF8.GetBytes(path)).ToLowerInvariant();
            moduleIds.Add(module, moduleId);
            foreach (var declaration in module.Documentation.Declarations)
            {
                var comment = module.Documentation.Comments.GetValueOrDefault(declaration.Node);
                if (declaration.Node is Variable && comment is null)
                    continue;
                var entry = new DocumentationEntry(module, declaration, comment,
                    moduleId + "-" + declaration.Kind + "-" + Convert.ToHexString(Encoding.UTF8.GetBytes(declaration.Name)).ToLowerInvariant());
                Entries.Add(entry);
                entriesByNode.Add(declaration.Node, entry);
            }
        }
    }
    public string ModuleId(Module module) => moduleIds[module];
    public DocumentationEntry? Find(Node node) => entriesByNode.GetValueOrDefault(node);
    public IEnumerable<Diagnostic> ValidateReferences(Module module, DocumentationText text, MarkdownDocument document)
    {
        foreach (var reference in document.Descendants<DocumentationReference>())
        {
            if (Resolve(module, reference.Name) is null)
                yield return text.Error("RUK6001", $"Unknown documentation reference '{reference.Name}'.", reference.Span.Start);
        }
    }

    public DocumentationEntry? Resolve(Module module, string name)
    {
        var parts = name.Split('.');
        if (parts.Length > 1 && module.Imports.TryGetValue(parts[0], out var imported))
        {
            module = imported;
            name = string.Join('.', parts.Skip(1));
        }
        var matches = Entries.Where(entry => entry.Module == module && entry.Declaration.Name == name).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}

internal static class Documentation
{
    private const int Success = 0;
    private const int DocumentationFailure = 1;
    private const int UsageOrIoFailure = 2;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var check = args is [_, "--check"];
        if (args is not ([_] or [_, "--check"] or [_, "--output", _]) ||
            !Path.GetExtension(args[0]).Equals(".rukbat", StringComparison.OrdinalIgnoreCase) ||
            args.Length == 3 && !Path.GetExtension(args[2]).Equals(".html", StringComparison.OrdinalIgnoreCase))
        {
            stderr.WriteLine("Usage: rukbat doc input.rukbat [--output reference.html | --check]");
            return UsageOrIoFailure;
        }
        string? stage = null;
        try
        {
            var input = Path.GetFullPath(args[0]);
            var output = args.Length == 3 ? Path.GetFullPath(args[2]) : Path.ChangeExtension(input, ".html");
            var program = Compiler.CheckCore(File.ReadAllText(input), input, true);
            if (program.Diagnostics.Count != 0)
            {
                Cli.WriteDiagnostics(program.Diagnostics, stderr);
                return DocumentationFailure;
            }
            var modules = program.Modules!.Where(module => module.StandardName is null).ToArray();
            var catalog = new DocumentationCatalog(modules, Path.GetDirectoryName(input)!);
            var diagnostics = new List<Diagnostic>();
            var documents = new Dictionary<DocumentationText, MarkdownDocument>();
            foreach (var module in modules)
            {
                diagnostics.AddRange(module.Documentation.Diagnostics);
                foreach (var text in module.Documentation.Texts)
                {
                    var document = DocumentationMarkdown.Parse(text);
                    documents.Add(text, document);
                    diagnostics.AddRange(catalog.ValidateReferences(module, text, document));
                    foreach (var code in document.Descendants<FencedCodeBlock>().Where(DocumentationMarkdown.IsChecked))
                        CheckExample(text, code, diagnostics);
                }
            }
            if (diagnostics.Count != 0)
            {
                Cli.WriteDiagnostics(diagnostics, stderr);
                return DocumentationFailure;
            }
            if (check)
                return Success;
            var html = Render(modules, program.Checker!, catalog, documents, Path.GetDirectoryName(input)!);
            stage = Path.Combine(Path.GetDirectoryName(output)!, ".rukbat-doc-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(stage, html, new UTF8Encoding(false));
            File.Move(stage, output, overwrite: true);
            stage = null;
            stdout.WriteLine(output);
            return Success;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            stderr.WriteLine($"error: {error.Message}");
            return UsageOrIoFailure;
        }
        finally
        {
            if (stage is not null && File.Exists(stage))
                File.Delete(stage);
        }
    }

    private static void CheckExample(DocumentationText text, FencedCodeBlock code, List<Diagnostic> diagnostics)
    {
        var lines = code.Lines.Count == 0 ? [] : code.Lines.Lines.Take(code.Lines.Count).ToArray();
        var snippet = string.Join("\n", lines.Select(line => line.ToString()));
        var virtualPath = Path.Combine(Path.GetDirectoryName(text.Source.Path)!, ".rukbat-doc-" + Guid.NewGuid().ToString("N") + ".rukbat");
        var program = Compiler.CheckCore(snippet, virtualPath, true);
        foreach (var diagnostic in program.Diagnostics)
        {
            if (diagnostic.FilePath != virtualPath)
            {
                diagnostics.Add(diagnostic);
                continue;
            }
            var lineIndex = Math.Clamp(diagnostic.Line - 1, 0, Math.Max(0, lines.Length - 1));
            var offset = lines.Length == 0 ? code.Span.Start : lines[lineIndex].Slice.Start + diagnostic.Column - 1;
            diagnostics.Add(text.Error(diagnostic.Code, "Documentation example: " + diagnostic.Message, offset));
        }
    }

    private static string Render(Module[] modules, TypeChecker checker, DocumentationCatalog catalog,
        Dictionary<DocumentationText, MarkdownDocument> documents, string root)
    {
        var title = Path.GetFileName(modules[^1].Source.Path);
        var html = new StringBuilder("<!doctype html>\n<html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">\n");
        html.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\">");
        html.Append("<title>").Append(Escape(title)).Append(" — Rukbat documentation</title><style>")
            .Append("body{font:16px/1.6 system-ui,sans-serif;max-width:960px;margin:auto;padding:24px;color:#243047;background:#fff}a{color:#245ac0}pre{overflow:auto;padding:16px;background:#f3f5f8;border-radius:6px}code{font-family:Consolas,monospace}section{border-top:1px solid #ddd;margin-top:32px}article{margin:24px 0}.source{font-size:.85em;color:#58657a}nav ul{columns:2}h1,h2,h3{line-height:1.3}")
            .Append("</style></head><body><h1>").Append(Escape(title)).Append("</h1>");
        // Lead with the entry script's usage, before the API inventory.
        var entryModule = modules[^1];
        if (entryModule.Documentation.Overview is { } entryOverview)
            html.Append(Body(entryModule, entryOverview));
        html.Append("<nav aria-label=\"Documentation index\"><h2>Index</h2><ul>");
        foreach (var module in modules.OrderBy(m => Relative(m), StringComparer.Ordinal))
            html.Append("<li><a href=\"#").Append(catalog.ModuleId(module)).Append("\">").Append(Escape(Relative(module))).Append("</a></li>");
        foreach (var entry in catalog.Entries.OrderBy(e => e.Declaration.Name, StringComparer.Ordinal).ThenBy(e => Relative(e.Module), StringComparer.Ordinal))
            html.Append("<li><a href=\"#").Append(entry.Id).Append("\">").Append(Escape(entry.Declaration.Name)).Append("</a> <small>").Append(Escape(Relative(entry.Module))).Append("</small></li>");
        html.Append("</ul></nav>");
        foreach (var module in new[] { entryModule }.Concat(modules.Where(m => m != entryModule).OrderBy(m => Relative(m), StringComparer.Ordinal)))
        {
            html.Append("<section id=\"").Append(catalog.ModuleId(module)).Append("\"><h2>").Append(Escape(Relative(module))).Append("</h2>");
            if (module != entryModule && module.Documentation.Overview is { } overview)
                html.Append(Body(module, overview));
            foreach (var entry in catalog.Entries.Where(entry => entry.Module == module))
            {
                html.Append("<article id=\"").Append(entry.Id).Append("\"><h3>").Append(Escape(entry.Declaration.Name)).Append("</h3><pre><code>")
                    .Append(Signature(entry)).Append("</code></pre><p class=\"source\">")
                    .Append(Escape(Relative(module))).Append(':').Append(new EditorText(module.Source.Text).Position(entry.Declaration.Node.Token.Offset).Line + 1);
                if (entry.Declaration.Node is Variable)
                    html.Append(entry.Declaration.Node is Variable { IsConst: true } ? " · Module-local constant" : " · Module-local variable");
                html.Append("</p>");
                if (entry.Comment is { } comment)
                    html.Append(Body(module, comment));
                html.Append("</article>");
            }
            html.Append("</section>");
        }
        return html.Append("</body></html>\n").ToString();

        string Relative(Module module) => Path.GetRelativePath(root, module.Source.Path).Replace('\\', '/');
        string Body(Module module, DocumentationText text) => DocumentationMarkdown.Render(documents[text], reference =>
            catalog.Resolve(module, reference.Name) is { } target ? "#" + target.Id : null);
        string Type(ValueType type)
        {
            if (type.NullableElement is { } nullable)
                return Type(nullable) + "?";
            if (type.Element is { } element)
                return Type(element) + "[]";
            if (type.DictionaryKey is { } key)
                return "dict[" + Type(key) + ", " + Type(type.DictionaryValue!) + "]";
            if (type.Struct is { } structure && catalog.Find(structure.Declaration) is { } target)
                return "<a href=\"#" + target.Id + "\">" + Escape(type.Name) + "</a>";
            return Escape(type.Name);
        }
        string Signature(DocumentationEntry entry) => entry.Declaration.Node switch
        {
            Function function => "fn " + Escape(function.Name) + "(" + string.Join(", ", function.Parameters.Select(parameter =>
                Escape(parameter.Token.Text) + ": " + Type(checker.DeclaredTypes[parameter]))) + "): " + Type(checker.Functions[function].ReturnType),
            StructDeclaration structure => "struct " + Escape(structure.Token.Text),
            Field field => Escape(field.Token.Text) + ": " + Type(checker.DeclaredTypes[field]),
            Variable variable => (variable.IsConst ? "const " : "") + Escape(variable.Token.Text) + ": " + Type(checker.Variables[variable].Type),
            _ => throw new InvalidOperationException("Unsupported documentation declaration.")
        };
    }
    private static string Escape(string text) => WebUtility.HtmlEncode(text);
}
