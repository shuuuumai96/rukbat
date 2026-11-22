using System.Text;

namespace Rukbat
{
    internal sealed class Lexer(Source source)
    {
        private const string SingleCharacterTokens = "(){}[]:,.=+-*/%!<>;?";
        private static readonly IReadOnlyDictionary<char, char> Escapes = new Dictionary<char, char>
        {
            ['n'] = '\n',
            ['r'] = '\r',
            ['t'] = '\t',
            ['\\'] = '\\',
            ['\''] = '\'',
            ['"'] = '"'
        };

        private int position;
        public List<Token> Scan()
        {
            var tokens = new List<Token>();
            while (position < source.Text.Length)
            {
                var start = position;
                var c = source.Text[position++];
                if (c == '\n')
                {
                    tokens.Add(new("\n", start));
                    continue;
                }
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                if (c == '/' && Peek() == '/')
                {
                    while (position < source.Text.Length && Peek() != '\n')
                    {
                        position++;
                    }
                    source.Comments.Add(new(start, position, source.Text[start..position].TrimEnd('\r')));
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    while (char.IsLetterOrDigit(Peek()) || Peek() == '_')
                    {
                        position++;
                    }

                    tokens.Add(new(source.Text[start..position], start));
                }
                else if (char.IsAsciiDigit(c))
                {
                    while (char.IsAsciiDigit(Peek()))
                    {
                        position++;
                    }

                    if (Peek() == '.' && position + 1 < source.Text.Length &&
                        char.IsAsciiDigit(source.Text[position + 1]))
                    {
                        position++;
                        while (char.IsAsciiDigit(Peek()))
                        {
                            position++;
                        }
                    }

                    if (Peek() is 'e' or 'E')
                    {
                        position++;
                        if (Peek() is '+' or '-')
                        {
                            position++;
                        }
                        if (!char.IsAsciiDigit(Peek()))
                        {
                            source.Error(DiagnosticCode.InvalidLiteral, "Float exponent requires at least one digit.", new("", start));
                        }
                        while (char.IsAsciiDigit(Peek()))
                        {
                            position++;
                        }
                    }

                    tokens.Add(new(source.Text[start..position], start));
                }
                else if (c is '\'' or '"')
                {
                    var value = new StringBuilder();
                    var closed = false;
                    while (position < source.Text.Length)
                    {
                        var next = source.Text[position++];
                        if (next == c)
                        {
                            closed = true;
                            break;
                        }
                        if (next == '\\')
                        {
                            if (position == source.Text.Length)
                            {
                                break;
                            }

                            next = source.Text[position++];
                            if (Escapes.TryGetValue(next, out var decoded))
                            {
                                next = decoded;
                            }
                            else
                            {
                                source.Error(DiagnosticCode.SyntaxError, $"Unsupported escape '\\{next}'.", new("", position - 2));
                            }
                        }
                        value.Append(next);
                    }
                    if (!closed)
                    {
                        source.Error(DiagnosticCode.SyntaxError, "Unterminated string.", new("", start));
                    }

                    tokens.Add(new(source.Text[start..position], start, value.ToString()));
                }
                else
                {
                    var pair = c.ToString() + Peek();
                    if (TokenFacts.IsOperator(pair))
                    {
                        position++;
                        tokens.Add(new(pair, start));
                    }
                    else if (SingleCharacterTokens.Contains(c))
                    {
                        tokens.Add(new(c.ToString(), start));
                    }
                    else
                    {
                        source.Error(DiagnosticCode.SyntaxError, c == '$'
                        ? "Rukbat variables have no '$' prefix. Use 'name: string = ...'."
                        : $"Unexpected character '{c}'.", new(c.ToString(), start));
                    }
                }
            }
            tokens.Add(new(TokenFacts.EndOfFile, source.Text.Length));
            source.FirstCodeOffset = tokens.FirstOrDefault(token => token.Text is not ("\n" or TokenFacts.EndOfFile))?.Offset ?? source.Text.Length;
            return tokens;
        }

        private char Peek() => position < source.Text.Length ? source.Text[position] : '\0';
    }
}
