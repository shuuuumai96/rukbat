namespace Rukbat;

internal sealed record EditorParameter(string Label, int Start, int End);
internal sealed record EditorCallable(string Label, string Documentation, IReadOnlyList<EditorParameter> Parameters, bool Variadic)
{
    public static EditorCallable? From(EditorSymbol symbol)
    {
        var open = symbol.Detail.IndexOf('(');
        var close = symbol.Detail.IndexOf(')', open + 1);
        if (open < 0 || close < 0)
            return null;
        var parameters = new List<EditorParameter>();
        var start = open + 1;
        while (start < close)
        {
            while (start < close && char.IsWhiteSpace(symbol.Detail[start]))
                start++;
            if (start == close)
                break;
            var end = start;
            var depth = 0;
            while (end < close)
            {
                var character = symbol.Detail[end];
                if (character == '[') depth++;
                else if (character == ']') depth--;
                else if (character == ',' && depth == 0) break;
                end++;
            }
            var label = symbol.Detail[start..end].TrimEnd();
            parameters.Add(new(label, start, start + label.Length));
            start = end + 1;
        }
        return new(symbol.Detail, symbol.Documentation, parameters, parameters.LastOrDefault()?.Label == "...");
    }
}

internal static class SignatureHelp
{
    private sealed class Delimiter(Token token, string closing, Token? callee = null)
    {
        public Token Token { get; } = token;
        public string Closing { get; } = closing;
        public Token? Callee { get; } = callee;
    }

    public static object? Get(string text, string path, int offset, Func<string, string> readSource, bool labelOffsets, bool markdown)
    {
        var source = new Source(text, path);
        var tokens = new Lexer(source).Scan();
        if (source.Comments.Any(c => offset >= c.Start && offset <= c.End) ||
            tokens.Any(t => t.StringValue is not null && offset > t.Offset && offset < t.Offset + t.Text.Length))
            return null;
        var stack = new Stack<Delimiter>();
        for (var i = 0; i < tokens.Count && tokens[i].Offset < offset; i++)
        {
            var token = tokens[i];
            if (token.StringValue is not null)
                continue;
            if (token.Text is "(" or "[" or "{")
            {
                Token? callee = null;
                if (token.Text == "(")
                {
                    var preceding = i - 1;
                    while (preceding >= 0 && tokens[preceding].Text == "\n")
                        preceding--;
                    if (preceding >= 0 && tokens[preceding].Text == ">")
                    {
                        while (preceding >= 0 && tokens[preceding].Text != "<")
                            preceding--;
                        preceding--;
                    }
                    if (preceding >= 0 && tokens[preceding] is { StringValue: null } name && name.Text.Length > 0 &&
                        (char.IsLetter(name.Text[0]) || name.Text[0] == '_') &&
                        (!Parser.IsReservedWord(name.Text) || EditorCatalog.Builtins.Any(b => b.Name == name.Text)))
                        callee = name;
                }
                stack.Push(new(token, token.Text == "(" ? ")" : token.Text == "[" ? "]" : "}", callee));
            }
            else if (token.Text is ")" or "]" or "}")
            {
                if (stack.Count == 0 || stack.Peek().Closing != token.Text)
                    return null;
                stack.Pop();
            }
        }
        var active = stack.FirstOrDefault(frame => frame.Callee is not null);
        if (active is null)
            return null;
        // Bind the real receiver/callee with an empty argument list in an ephemeral copy.
        // Original offsets through the opening parenthesis are unchanged. No diagnostics escape.
        var prefix = text[..(active.Token.Offset + 1)];
        var prefixTokens = new Lexer(new Source(prefix, path)).Scan();
        var closings = new Stack<string>();
        foreach (var token in prefixTokens)
        {
            if (token.StringValue is not null)
                continue;
            if (token.Text is "(" or "[" or "{")
                closings.Push(token.Text == "(" ? ")" : token.Text == "[" ? "]" : "}");
            else if (closings.Count > 0 && token.Text == closings.Peek())
                closings.Pop();
        }
        var analysis = new LanguageAnalysis(text, path, readSource, includeExamples: false);
        var callSite = analysis.Entry.CallSites?.Values.FirstOrDefault(s => s.Opening.Offset == active.Token.Offset && s.Opening.Text == "(");
        if (callSite is null)
            return null;
        var argument = callSite.Separators.Count(t => t.Offset < offset);
        var symbol = analysis.At(active.Callee!.Offset)?.Symbol;
        if (symbol is null)
        {
            var closing = MatchingClose(tokens, active.Token.Offset);
            var repaired = closing is not null ? prefix + text[closing.Offset..] : prefix + string.Join("\n", closings);
            if (closing is null)
            {
                // Top-level declarations after an unfinished call remain available during authoring.
                var laterDeclaration = tokens.FirstOrDefault(t => t.Offset >= offset && t.StringValue is null && t.Text is "fn" or "struct");
                if (laterDeclaration is not null)
                    repaired += "\n" + text[laterDeclaration.Offset..];
            }
            analysis = new LanguageAnalysis(repaired, path, readSource, includeExamples: false);
            symbol = analysis.At(active.Callee.Offset)?.Symbol ?? analysis.StandardCallable(active.Callee.Offset);
        }
        var callable = symbol is null ? null : EditorCatalog.Signatures.GetValueOrDefault(symbol.Detail) ?? EditorCallable.From(symbol);
        if (callable is null || callable.Parameters.Count != 0 && argument >= callable.Parameters.Count && !callable.Variadic)
            return null;
        var information = new Dictionary<string, object?>
        {
            ["label"] = callable.Label,
            ["documentation"] = markdown ? new { kind = "markdown", value = callable.Documentation } : callable.Documentation,
            ["parameters"] = callable.Parameters.Select(p => new { label = labelOffsets ? (object)new[] { p.Start, p.End } : p.Label }).ToArray()
        };
        var result = new Dictionary<string, object?> { ["signatures"] = new[] { information }, ["activeSignature"] = 0 };
        if (callable.Parameters.Count != 0)
        {
            var parameter = Math.Min(argument, callable.Parameters.Count - 1);
            information["activeParameter"] = parameter;
            result["activeParameter"] = parameter;
        }
        return result;
    }

    private static Token? MatchingClose(IReadOnlyList<Token> tokens, int opening)
    {
        var stack = new Stack<string>();
        foreach (var token in tokens.Where(t => t.Offset >= opening && t.StringValue is null))
        {
            if (token.Text is "(" or "[" or "{")
                stack.Push(token.Text == "(" ? ")" : token.Text == "[" ? "]" : "}");
            else if (token.Text is ")" or "]" or "}")
            {
                if (stack.Count == 0 || stack.Pop() != token.Text)
                    return null;
                if (stack.Count == 0)
                    return token;
            }
        }
        return null;
    }
}

internal sealed partial class LanguageAnalysis
{
    public EditorSymbol? StandardCallable(int offset)
    {
        // Some inferred standard operations cannot type-check an empty argument list.
        // Resolve only actual bound standard imports, never an arbitrary spelling match.
        var tokens = new Lexer(new Source(Entry.Source.Text, Entry.Source.Path)).Scan();
        var index = tokens.FindIndex(t => t.Offset == offset);
        if (index < 2 || tokens[index - 1].Text != "." || !Entry.Imports.TryGetValue(tokens[index - 2].Text, out var module) || module.StandardName is null)
            return null;
        var generic = index + 1 < tokens.Count && tokens[index + 1].Text == "<";
        var candidates = EditorCatalog.Standard.Where(s => s.Name == tokens[index].Text && s.Detail.StartsWith(module.StandardName + ".", StringComparison.Ordinal));
        return candidates.FirstOrDefault(s => s.Detail.Contains('<') == generic);
    }
}
