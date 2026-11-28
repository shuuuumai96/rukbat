using System.Text;

namespace Rukbat
{
    internal sealed record FormatResult(string? Output, IReadOnlyList<Diagnostic> Diagnostics)
    {
        public bool Success => Output is not null && Diagnostics.Count == 0;
    }

    internal static class Formatter
    {
        private enum ItemKind { Token, Comment, Newline }

        private sealed record Item(string Text, int Offset, ItemKind Kind, bool PreserveTrailingSpace = false);

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

            return new(new Writer(Items(source, tokens), syntax).Write(), []);
        }

        private static List<Item> Items(Source source, List<Token> tokens)
        {
            var items = new List<Item>();
            var end = 0;
            var commentIndex = 0;
            foreach (var token in tokens)
            {
                // Each line comment ends at the next newline token or at EOF.
                if (commentIndex < source.Comments.Count && source.Comments[commentIndex].Start < token.Offset)
                {
                    var comment = source.Comments[commentIndex++];
                    var isDocumentation = comment.IsDocumentation;
                    var commentText = isDocumentation ? comment.Text : comment.Text.TrimEnd();
                    items.Add(new(commentText, end, ItemKind.Comment, PreserveTrailingSpace: isDocumentation));
                }

                if (token.Text == TokenFacts.EndOfFile)
                {
                    break;
                }

                // Escape physical line breaks without altering the string's value.
                var text = token.StringValue is null ? token.Text
                    : token.Text.Replace("\r", "\\r").Replace("\n", "\\n");
                items.Add(new(text, token.Offset, token.Text == "\n" ? ItemKind.Newline : ItemKind.Token));
                end = token.Offset + token.Text.Length;
            }
            return items;
        }

        private sealed class Writer(List<Item> items, FormattingSyntax syntax)
        {
            private const int IndentationWidth = 4;
            private const int TrailingCommentSpaces = 2;

            private static readonly IReadOnlyDictionary<ItemKind, Action<Writer, Item>> ItemWriters =
                new Dictionary<ItemKind, Action<Writer, Item>>
                {
                    [ItemKind.Token] = static (writer, item) => writer.WriteToken(item),
                    [ItemKind.Comment] = static (writer, item) => writer.WriteComment(item),
                    [ItemKind.Newline] = static (writer, item) => writer.WriteSourceNewline(item)
                };

            private static readonly Action<Writer, Item> WriteWordToken = static (writer, item) => writer.WriteWord(item);
            private static readonly Action<Writer, Item> WriteBinaryOperator = static (writer, item) => writer.WriteOperator(item);
            private static readonly Action<Writer, Item> WriteTightToken = static (writer, item) => writer.WriteTight(item);

            private static readonly IReadOnlyDictionary<string, Action<Writer, Item>> TokenWriters =
                new Dictionary<string, Action<Writer, Item>>(StringComparer.Ordinal)
                {
                    ["{"] = static (writer, item) => writer.OpenBlock(item),
                    ["}"] = static (writer, item) => writer.CloseBlock(item),
                    [";"] = static (writer, _) => writer.EndStatement(),
                    ["("] = static (writer, item) => writer.OpenContinuation(item),
                    ["["] = static (writer, item) => writer.OpenContinuation(item),
                    [")"] = static (writer, item) => writer.CloseContinuation(item),
                    ["]"] = static (writer, item) => writer.CloseContinuation(item),
                    [","] = static (writer, item) => writer.WriteSeparator(item),
                    [":"] = static (writer, item) => writer.WriteSeparator(item),
                    ["."] = WriteTightToken,
                    ["?"] = WriteTightToken,
                    ["<"] = static (writer, item) => writer.WriteAngle(item),
                    [">"] = static (writer, item) => writer.WriteAngle(item),
                    ["!"] = WriteWordToken,
                    ["+"] = static (writer, item) => writer.WriteSign(item),
                    ["-"] = static (writer, item) => writer.WriteSign(item)
                };

            private readonly StringBuilder output = new();
            private readonly StringBuilder line = new();
            private readonly Stack<int> continuations = new();
            private readonly Dictionary<int, int> continuationCounts = [];
            private int index;
            private int indentation;
            private bool commentContinuation;
            private int sourceNewlines;
            private bool pendingBlankLine;
            private string? lastContent;
            private string? previous;

            public string Write()
            {
                var requiredBlankLines = BlankLinePositions(items, syntax);
                for (index = 0; index < items.Count; index++)
                {
                    var item = items[index];
                    if (requiredBlankLines.Contains(index))
                    {
                        NewLine();
                        pendingBlankLine = true;
                    }

                    ItemWriters[item.Kind](this, item);
                }

                NewLine();
                return output.Length == 0 ? "\n" : output.ToString();
            }

            private void WriteToken(Item item)
            {
                sourceNewlines = 0;
                TokenWriters.GetValueOrDefault(item.Text, WriteUnregisteredToken)(this, item);
                previous = item.Text == ";" ? null : item.Text;
                lastContent = item.Text;
            }

            private void WriteComment(Item item)
            {
                sourceNewlines = 0;
                if (line.Length != 0)
                {
                    TrimSpace(line);
                    line.Append(' ', TrailingCommentSpaces);
                }
                Append(item.Text);
                lastContent = item.Text;
                NewLine(item.PreserveTrailingSpace);

                commentContinuation |= index + 1 < items.Count &&
                    syntax.ContinuationNewlines.Contains(items[index + 1].Offset) &&
                    previous is not (null or "{" or "}");
            }

            private void WriteSourceNewline(Item item)
            {
                sourceNewlines++;
                if (syntax.ContinuationNewlines.Contains(item.Offset))
                {
                    return;
                }

                NewLine();
                previous = null;
                commentContinuation = false;
                pendingBlankLine |= sourceNewlines > 1;
            }

            private void OpenBlock(Item item)
            {
                commentContinuation = false;
                Append(item.Text, spaceBefore: true, indentContinuation: false);
                NewLine();
                indentation++;
            }

            private void CloseBlock(Item item)
            {
                NewLine();
                indentation = Math.Max(0, indentation - 1);
                Append(item.Text, indentContinuation: false);
                if (syntax.ExpressionClosings.Contains(item.Offset))
                {
                    return;
                }

                if (NextToken(items, index) is "else" or "catch")
                {
                    Space();
                    return;
                }

                if (index + 1 >= items.Count || items[index + 1].Kind != ItemKind.Comment)
                {
                    NewLine();
                }
            }

            private void EndStatement()
            {
                NewLine();
                commentContinuation = false;
            }

            private void OpenContinuation(Item item)
            {
                Append(item.Text, syntax.ExpressionOpenings.Contains(item.Offset) && NeedsWordSpace(previous));
                continuations.Push(indentation);
                continuationCounts[indentation] = continuationCounts.GetValueOrDefault(indentation) + 1;
            }

            private void CloseContinuation(Item item)
            {
                var depth = continuations.Pop();
                if (continuationCounts[depth] == 1)
                {
                    continuationCounts.Remove(depth);
                }
                else
                {
                    continuationCounts[depth]--;
                }

                TrimSpace(line);
                Append(item.Text, indentContinuation: false);
            }

            private void WriteTight(Item item)
            {
                TrimSpace(line);
                Append(item.Text);
            }

            private void WriteSeparator(Item item)
            {
                WriteTight(item);
                Space();
            }

            private void WriteWord(Item item) => Append(item.Text, NeedsWordSpace(previous));

            private void WriteOperator(Item item)
            {
                Space();
                Append(item.Text);
                Space();
            }

            private void WriteAngle(Item item)
            {
                var write = syntax.GenericDelimiters.Contains(item.Offset) ? WriteTightToken : WriteBinaryOperator;
                write(this, item);
            }

            private void WriteSign(Item item)
            {
                var write = syntax.UnaryOperators.Contains(item.Offset) ? WriteWordToken : WriteBinaryOperator;
                write(this, item);
            }

            private static void WriteUnregisteredToken(Writer writer, Item item)
            {
                var write = TokenFacts.IsOperator(item.Text) ? WriteBinaryOperator : WriteWordToken;
                write(writer, item);
            }

            private void Append(string value, bool spaceBefore = false, bool indentContinuation = true)
            {
                var atLineStart = line.Length == 0;
                if (pendingBlankLine && atLineStart && output.Length != 0 && lastContent != "{" && value != "}")
                {
                    output.Append('\n');
                }
                pendingBlankLine = false;

                var spaces = atLineStart
                    ? LineIndentationWidth(indentContinuation)
                    : spaceBefore && line[^1] != ' ' ? 1 : 0;
                line.Append(' ', spaces);
                line.Append(value);
            }

            private int LineIndentationWidth(bool indentContinuation)
            {
                var continued = indentContinuation &&
                    (commentContinuation || continuationCounts.ContainsKey(indentation));
                return (indentation + (continued ? 1 : 0)) * IndentationWidth;
            }

            private void Space()
            {
                if (line.Length != 0 && line[^1] != ' ')
                {
                    line.Append(' ');
                }
            }

            private void NewLine(bool preserveTrailingSpace = false)
            {
                if (!preserveTrailingSpace)
                {
                    TrimSpace(line);
                }
                if (line.Length != 0)
                {
                    output.Append(line).Append('\n');
                    line.Clear();
                }
            }
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
                if (!syntax.RequiredBlankLines.Contains(items[index].Offset) || items[index].Kind != ItemKind.Token)
                {
                    continue;
                }
                var start = index;
                while (start > 0 && items[start - 1].Kind != ItemKind.Token)
                {
                    start--;
                }
                // Keep a trailing comment with the previous statement; a leading
                // comment belongs to the statement following it.
                if (start < index && items[start].Kind == ItemKind.Comment)
                {
                    start++;
                }
                while (start < index && items[start].Kind == ItemKind.Newline)
                {
                    start++;
                }
                positions.Add(start);
            }
            return positions;
        }

        private static bool NeedsWordSpace(string? previous) => previous is not null &&
            previous is not ("(" or "[" or "." or "<" or "!" or "+" or "-") &&
            !TokenFacts.IsOperator(previous);

        private static string? NextToken(List<Item> items, int index)
        {
            for (var next = index + 1; next < items.Count; next++)
            {
                if (items[next].Kind != ItemKind.Newline)
                {
                    return items[next].Kind == ItemKind.Comment ? null : items[next].Text;
                }
            }
            return null;
        }
    }
}
