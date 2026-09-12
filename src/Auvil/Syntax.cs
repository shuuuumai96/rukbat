using System.Management.Automation.Language;

namespace Auvil;

public sealed record Diagnostic(string Code, string Message, int Line, int Column);
public sealed record CompilationResult(string? Output, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Output is not null && Diagnostics.Count == 0;
}

internal enum SimpleType { Unknown, String, Int, Bool, Other, Void }
internal sealed record Annotation(int VariableOffset, int Start, int End, SimpleType Type);
internal sealed record ReturnAnnotation(int FunctionOffset, int OpenParen, int CloseParen,
    int Brace, int Start, int End, SimpleType Type);
internal sealed record Edit(int Start, int End, string Text);

// Offsets remain those of the original file throughout analysis.
internal sealed class Syntax(string source)
{
    public string Source { get; } = source;
    public List<Annotation> Variables { get; } = [];
    public List<ReturnAnnotation> Returns { get; } = [];
    public List<Diagnostic> Diagnostics { get; } = [];

    public void Error(string code, string message, int offset)
    {
        var prefix = Source[..Math.Clamp(offset, 0, Source.Length)];
        Diagnostics.Add(new(code, message, prefix.Count(c => c == '\n') + 1,
            prefix.Length - prefix.LastIndexOf('\n')));
    }

    private SimpleType ReadType(Token token)
    {
        var type = token.Text.ToLowerInvariant() switch
        {
            "string" => SimpleType.String,
            "int" => SimpleType.Int,
            "bool" => SimpleType.Bool,
            _ => SimpleType.Unknown
        };
        if (type == SimpleType.Unknown)
            Error("AUV1001", $"Unsupported type '{token.Text}'. Expected string, int or bool.", token.Extent.StartOffset);
        return type;
    }

    public ScriptBlockAst Parse()
    {
        Parser.ParseInput(Source, out var allTokens, out _);
        var tokens = allTokens.Where(t => t.Kind is not TokenKind.Comment and not TokenKind.NewLine).ToArray();
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            var token = tokens[i];
            // A scoped variable ($env:PATH) is deliberately not an annotation.
            // The annotation grammar requires whitespace after ':'.
            if (token.Kind == TokenKind.Variable && token.Text.EndsWith(':'))
            {
                var next = tokens[i + 1];
                Variables.Add(new(token.Extent.StartOffset, token.Extent.EndOffset - 1,
                    next.Extent.EndOffset, ReadType(next)));
            }
            if (token.Kind != TokenKind.Function || i + 2 >= tokens.Length || tokens[i + 2].Kind != TokenKind.LParen)
                continue;
            var open = i + 2;
            var depth = 1;
            var close = open;
            while (++close < tokens.Length && depth > 0)
            {
                if (tokens[close].Kind == TokenKind.LParen) depth++;
                if (tokens[close].Kind == TokenKind.RParen) depth--;
            }
            // The loop advances once after consuming the closing parenthesis.
            close--;
            if (depth != 0 || close + 3 >= tokens.Length || tokens[close + 1].Text != ":") continue;
            var typeToken = tokens[close + 2];
            var brace = tokens[close + 3];
            if (brace.Kind != TokenKind.LCurly)
                Error("AUV1002", "Expected '{' after return type.", brace.Extent.StartOffset);
            Returns.Add(new(token.Extent.StartOffset, tokens[open].Extent.StartOffset,
                tokens[close].Extent.StartOffset, brace.Extent.StartOffset,
                tokens[close + 1].Extent.StartOffset, typeToken.Extent.EndOffset, ReadType(typeToken)));
        }

        var chars = Source.ToCharArray();
        foreach (var (start, end) in Variables.Select(a => (a.Start, a.End)).Concat(Returns.Select(a => (a.Start, a.End))))
            for (var i = start; i < end; i++)
                if (chars[i] is not '\r' and not '\n') chars[i] = ' ';
        var ast = Parser.ParseInput(new string(chars), out _, out var errors);
        foreach (var error in errors) Error("AUV1000", error.Message, error.Extent.StartOffset);
        return ast;
    }

    public static string Apply(string source, IEnumerable<Edit> edits, int origin = 0)
    {
        foreach (var edit in edits.OrderByDescending(e => e.Start).ThenByDescending(e => e.End))
            source = source.Remove(edit.Start - origin, edit.End - edit.Start).Insert(edit.Start - origin, edit.Text);
        return source;
    }
}
