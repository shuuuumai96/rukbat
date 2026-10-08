using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Rukbat;

internal sealed class DocumentationReference(string name) : LinkInline
{
    public string Name { get; } = name;
}

// Only an unresolved shortcut [`name`] is a symbol reference. Ordinary links
// and reference definitions stay under CommonMark's own link parser.
internal sealed class DocumentationReferenceParser : InlineParser
{
    public DocumentationReferenceParser() => OpeningCharacters = ['['];
    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var start = slice.Start;
        if (start + 2 >= slice.End || slice.Text[start + 1] != '`')
            return false;
        var end = start + 2;
        while (end <= slice.End && (char.IsLetterOrDigit(slice.Text[end]) || slice.Text[end] is '_' or '.'))
            end++;
        if (end == start + 2 || end + 1 > slice.End || slice.Text[end] != '`' || slice.Text[end + 1] != ']')
            return false;
        if (end + 2 <= slice.End && slice.Text[end + 2] is '(' or '[')
            return false;
        // Defined CommonMark references take precedence over symbol shortcuts.
        var label = slice.Text[(start + 1)..(end + 1)];
        if (processor.Document.ContainsLinkReferenceDefinition(label))
            return false;
        var sourceStart = processor.GetSourcePosition(start, out var line, out var column);
        var reference = new DocumentationReference(slice.Text[(start + 2)..end])
        {
            Span = new(sourceStart, processor.GetSourcePosition(end + 1)), Line = line, Column = column
        };
        reference.AppendChild(new CodeInline(reference.Name));
        processor.Inline = reference;
        slice.Start = end + 2;
        return true;
    }
}

internal static class DocumentationMarkdown
{
    public static readonly MarkdownPipeline Pipeline = CreatePipeline();
    private static MarkdownPipeline CreatePipeline()
    {
        var builder = new MarkdownPipelineBuilder().DisableHtml().UsePreciseSourceLocation();
        builder.InlineParsers.Insert(0, new DocumentationReferenceParser());
        return builder.Build();
    }

    public static MarkdownDocument Parse(DocumentationText text) => Markdown.Parse(text.Markdown, Pipeline);
    public static bool IsChecked(FencedCodeBlock code) => code.Info == "rukbat" && code.Arguments?.Trim() == "check";

    public static string Render(MarkdownDocument document, Func<DocumentationReference, string?> resolve)
    {
        foreach (var reference in document.Descendants<DocumentationReference>())
            reference.Url = resolve(reference);
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (link.IsImage)
            {
                // Keep the caption but never load images while reading offline docs.
                link.IsImage = false;
                link.Url = null;
            }
            else if (link.Url is { } url && !SafeLink(url))
                link.Url = null;
        }
        foreach (var code in document.Descendants<FencedCodeBlock>().Where(IsChecked))
        {
            var note = new ParagraphBlock { Inline = new ContainerInline() };
            note.Inline.AppendChild(new LiteralInline("Statically checked; not executed."));
            code.Parent!.Insert(code.Parent.IndexOf(code) + 1, note);
        }
        return Markdown.ToHtml(document, Pipeline);
    }

    private static bool SafeLink(string url)
    {
        if (url.StartsWith('#'))
            return true;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto";
    }
}
