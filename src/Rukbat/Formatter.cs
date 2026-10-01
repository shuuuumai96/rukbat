using System.Text;

namespace Rukbat;

internal sealed record FormatResult(string? Output, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Output is not null && Diagnostics.Count == 0;
}

internal static class Formatter
{
    private sealed record Item(string Text, int Offset, bool IsComment = false, bool IsNewLine = false);

    public static FormatResult Format(string text, string path)
    {
        var source = new Source(text, path);
        var tokens = new Lexer(source).Scan();
        var syntax = new FormattingSyntax();
        if (source.Diagnostics.Count == 0)
        {
            _ = new Parser(source, tokens, syntax).Parse();
        }
        if (source.Diagnostics.Count != 0)
        {
            return new(null, source.Diagnostics);
        }

        return new(Write(Items(text, tokens), syntax), []);
    }

    private static List<Item> Items(string source, List<Token> tokens)
    {
        var items = new List<Item>();
        var end = 0;
        foreach (var token in tokens.Where(token => token.Text != "<eof>"))
        {
            if (token.Offset > end)
            {
                var gap = source[end..token.Offset].Trim();
                if (gap.StartsWith("#", StringComparison.Ordinal) ||
                    gap.StartsWith("//", StringComparison.Ordinal))
                {
                    items.Add(new(gap, end, IsComment: true));
                }
            }
            // Escape physical line breaks without altering the string's value.
            var text = token.StringValue is null ? token.Text
                : token.Text.Replace("\r", "\\r").Replace("\n", "\\n");
            items.Add(new(text, token.Offset, IsNewLine: token.Text == "\n"));
            end = token.Offset + token.Text.Length;
        }
        if (end < source.Length)
        {
            var gap = source[end..].Trim();
            if (gap.StartsWith("#", StringComparison.Ordinal) ||
                gap.StartsWith("//", StringComparison.Ordinal))
            {
                items.Add(new(gap, end, IsComment: true));
            }
        }
        return items;
    }

    private static string Write(List<Item> items, FormattingSyntax syntax)
    {
        const int indentationWidth = 4;
        var requiredBlankLines = BlankLinePositions(items, syntax);
        var output = new StringBuilder();
        var line = new StringBuilder();
        var indentation = 0;
        var continuations = new Stack<int>();
        var commentContinuation = false;
        var sourceNewlines = 0;
        var pendingBlankLine = false;
        string? lastContent = null;
        string? previous = null;

        void Append(string value, bool spaceBefore = false, bool indentContinuation = true)
        {
            if (pendingBlankLine)
            {
                if (line.Length == 0 && output.Length != 0 && lastContent != "{" && value != "}")
                {
                    output.Append('\n');
                }
                pendingBlankLine = false;
            }
            if (line.Length == 0)
            {
                var extra = indentContinuation &&
                    (commentContinuation || continuations.Any(depth => depth == indentation)) ? 1 : 0;
                line.Append(' ', (indentation + extra) * indentationWidth);
            }
            else if (spaceBefore && line[^1] != ' ')
            {
                line.Append(' ');
            }
            line.Append(value);
        }

        void Space()
        {
            if (line.Length != 0 && line[^1] != ' ')
            {
                line.Append(' ');
            }
        }

        void NewLine()
        {
            while (line.Length != 0 && line[^1] == ' ')
            {
                line.Length--;
            }
            if (line.Length != 0)
            {
                output.Append(line).Append('\n');
                line.Clear();
            }
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (requiredBlankLines.Contains(index))
            {
                NewLine();
                pendingBlankLine = true;
            }
            if (item.IsComment)
            {
                sourceNewlines = 0;
                if (line.Length != 0)
                {
                    TrimSpace(line);
                    line.Append("  ");
                }
                Append(item.Text);
                lastContent = item.Text;
                NewLine();
                if (index + 1 < items.Count && syntax.ContinuationNewlines.Contains(items[index + 1].Offset) &&
                    previous is not (null or "{" or "}"))
                {
                    commentContinuation = true;
                }
                continue;
            }
            if (item.IsNewLine)
            {
                sourceNewlines++;
                if (!syntax.ContinuationNewlines.Contains(item.Offset))
                {
                    NewLine();
                    previous = null;
                    commentContinuation = false;
                    if (sourceNewlines > 1)
                    {
                        pendingBlankLine = true;
                    }
                }
                continue;
            }

            var token = item.Text;
            sourceNewlines = 0;
            switch (token)
            {
                case "{":
                    commentContinuation = false;
                    Append("{", spaceBefore: true, indentContinuation: false);
                    NewLine();
                    indentation++;
                    break;
                case "}":
                    NewLine();
                    indentation = Math.Max(0, indentation - 1);
                    Append("}", indentContinuation: false);
                    if (syntax.ExpressionClosings.Contains(item.Offset))
                    {
                        break;
                    }
                    if (NextToken(items, index) is "else" or "catch")
                    {
                        Space();
                    }
                    else if (index + 1 >= items.Count || !items[index + 1].IsComment)
                    {
                        NewLine();
                    }
                    break;
                case ";":
                    NewLine();
                    commentContinuation = false;
                    break;
                case "(":
                    Append("(", syntax.ExpressionOpenings.Contains(item.Offset) && NeedsWordSpace(previous));
                    continuations.Push(indentation);
                    break;
                case ")":
                    continuations.Pop();
                    TrimSpace(line);
                    Append(")", indentContinuation: false);
                    break;
                case "[":
                    Append("[", syntax.ExpressionOpenings.Contains(item.Offset) && NeedsWordSpace(previous));
                    continuations.Push(indentation);
                    break;
                case "]":
                    continuations.Pop();
                    TrimSpace(line);
                    Append("]", indentContinuation: false);
                    break;
                case ",":
                    TrimSpace(line);
                    Append(",");
                    Space();
                    break;
                case ".":
                    TrimSpace(line);
                    Append(".");
                    break;
                case ":":
                    TrimSpace(line);
                    Append(":");
                    Space();
                    break;
                case "?":
                    TrimSpace(line);
                    Append("?");
                    break;
                case "<" when syntax.GenericDelimiters.Contains(item.Offset):
                    TrimSpace(line);
                    Append("<");
                    break;
                case ">" when syntax.GenericDelimiters.Contains(item.Offset):
                    TrimSpace(line);
                    Append(">");
                    break;
                case "!":
                    Append("!", NeedsWordSpace(previous));
                    break;
                case "+" or "-" when syntax.UnaryOperators.Contains(item.Offset):
                    Append(token, NeedsWordSpace(previous));
                    break;
                case ":=" or "=" or "==" or "!=" or "<=" or ">=" or "<" or ">" or
                     "+" or "-" or "*" or "/" or "%" or "&&" or "||":
                    Space();
                    Append(token);
                    Space();
                    break;
                default:
                    Append(token, NeedsWordSpace(previous));
                    break;
            }
            previous = token is ";" ? null : token;
            lastContent = token;
        }

        NewLine();
        output.Append('\n');
        return output.ToString();
    }

    private static void TrimSpace(StringBuilder line)
    {
        while (line.Length != 0 && line[^1] == ' ')
        {
            line.Length--;
        }
    }

    private static HashSet<int> BlankLinePositions(List<Item> items, FormattingSyntax syntax)
    {
        var positions = new HashSet<int>();
        for (var index = 0; index < items.Count; index++)
        {
            if (!syntax.RequiredBlankLines.Contains(items[index].Offset) || items[index].IsComment || items[index].IsNewLine)
            {
                continue;
            }
            var start = index;
            while (start > 0 && (items[start - 1].IsComment || items[start - 1].IsNewLine))
            {
                start--;
            }
            // Keep a trailing comment with the previous statement; a leading
            // comment belongs to the statement following it.
            if (start < index && items[start].IsComment)
            {
                start++;
            }
            while (start < index && items[start].IsNewLine)
            {
                start++;
            }
            positions.Add(start);
        }
        return positions;
    }

    private static bool NeedsWordSpace(string? previous) => previous is not null &&
        previous is not ("(" or "[" or "." or "<" or "!" or "+" or "-") &&
        !IsOperator(previous);

    private static bool IsOperator(string token) => token is
        ":=" or "=" or "==" or "!=" or "<=" or ">=" or "<" or ">" or
        "+" or "-" or "*" or "/" or "%" or "&&" or "||";

    private static string? NextToken(List<Item> items, int index)
    {
        for (var next = index + 1; next < items.Count; next++)
        {
            if (!items[next].IsNewLine)
            {
                return items[next].IsComment ? null : items[next].Text;
            }
        }
        return null;
    }
}
