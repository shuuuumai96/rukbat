using System.Text;

namespace Rukbat;

internal sealed record FormatResult(string? Output, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Output is not null && Diagnostics.Count == 0;
}

internal static class Formatter
{
    private sealed record Item(string Text, bool IsComment = false, bool IsNewLine = false);

    public static FormatResult Format(string text, string path)
    {
        var source = new Source(text, path);
        var tokens = new Lexer(source).Scan();
        if (source.Diagnostics.Count == 0)
        {
            _ = new Parser(source, tokens).Parse();
        }
        if (source.Diagnostics.Count != 0)
        {
            return new(null, source.Diagnostics);
        }

        return new(Write(Items(text, tokens)), []);
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
                    items.Add(new(gap, IsComment: true));
                }
            }
            items.Add(token.Text == "\n" ? new("\n", IsNewLine: true) : new(token.Text));
            end = token.Offset + token.Text.Length;
        }
        if (end < source.Length)
        {
            var gap = source[end..].Trim();
            if (gap.StartsWith("#", StringComparison.Ordinal) ||
                gap.StartsWith("//", StringComparison.Ordinal))
            {
                items.Add(new(gap, IsComment: true));
            }
        }
        return items;
    }

    private static string Write(List<Item> items)
    {
        const int indentationWidth = 4;
        var output = new StringBuilder();
        var line = new StringBuilder();
        var indentation = 0;
        var parentheses = 0;
        var brackets = 0;
        var genericDepth = 0;
        string? previous = null;

        void Append(string value, bool spaceBefore = false)
        {
            if (line.Length == 0)
            {
                line.Append(' ', indentation * indentationWidth);
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
                output.AppendLine(line.ToString());
                line.Clear();
            }
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.IsComment)
            {
                if (line.Length != 0)
                {
                    TrimSpace(line);
                    line.Append("  ");
                }
                Append(item.Text);
                NewLine();
                previous = null;
                continue;
            }
            if (item.IsNewLine)
            {
                if (previous == "}" && NextToken(items, index) is "else" or "catch")
                {
                    continue;
                }
                if (parentheses == 0 && brackets == 0 && genericDepth == 0 &&
                    previous is not null && previous is not ("{" or "," or ".") && !IsOperator(previous))
                {
                    NewLine();
                    previous = null;
                }
                continue;
            }

            var token = item.Text;
            switch (token)
            {
                case "{":
                    Append("{", spaceBefore: true);
                    NewLine();
                    indentation++;
                    break;
                case "}":
                    NewLine();
                    indentation = Math.Max(0, indentation - 1);
                    Append("}");
                    if (NextToken(items, index) is "else" or "catch")
                    {
                        Space();
                    }
                    else
                    {
                        NewLine();
                    }
                    break;
                case ";":
                    NewLine();
                    break;
                case "(":
                    Append("(");
                    parentheses++;
                    break;
                case ")":
                    TrimSpace(line);
                    Append(")");
                    parentheses--;
                    break;
                case "[":
                    Append("[");
                    brackets++;
                    break;
                case "]":
                    TrimSpace(line);
                    Append("]");
                    brackets--;
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
                case "<" when IsGenericOpen(items, index):
                    TrimSpace(line);
                    Append("<");
                    genericDepth++;
                    break;
                case ">" when genericDepth != 0:
                    TrimSpace(line);
                    Append(">");
                    genericDepth--;
                    break;
                case "!":
                    Append("!", NeedsWordSpace(previous));
                    break;
                case "+" or "-" when IsUnary(previous):
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
        }

        NewLine();
        return output.ToString().Replace("\r\n", "\n");
    }

    private static void TrimSpace(StringBuilder line)
    {
        while (line.Length != 0 && line[^1] == ' ')
        {
            line.Length--;
        }
    }

    private static bool NeedsWordSpace(string? previous) => previous is not null &&
        previous is not ("(" or "[" or "." or "<" or "!" or "+" or "-") &&
        !IsOperator(previous);

    private static bool IsUnary(string? previous) => previous is null or
        "(" or "[" or "," or ":" or ":=" or "=" or "==" or "!=" or "<=" or ">=" or
        "<" or ">" or "+" or "-" or "*" or "/" or "%" or "&&" or "||" or "return" or "throw";

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

    private static bool IsGenericOpen(List<Item> items, int index)
    {
        var previous = PreviousToken(items, index);
        if (previous is not ("powershell" or "decode"))
        {
            return false;
        }
        var depth = 1;
        for (var next = index + 1; next < items.Count; next++)
        {
            if (items[next].IsComment || items[next].IsNewLine)
            {
                continue;
            }
            if (items[next].Text == "<") depth++;
            if (items[next].Text == ">" && --depth == 0)
            {
                return NextToken(items, next) == "(";
            }
        }
        return false;
    }

    private static string? PreviousToken(List<Item> items, int index)
    {
        for (var previous = index - 1; previous >= 0; previous--)
        {
            if (!items[previous].IsNewLine && !items[previous].IsComment)
            {
                return items[previous].Text;
            }
        }
        return null;
    }
}
