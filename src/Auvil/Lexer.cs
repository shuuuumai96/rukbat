using System.Text;

namespace Auvil;

internal sealed class Lexer(Source source)
{
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

            if (c == '#' || (c == '/' && Peek() == '/'))
            {
                while (position < source.Text.Length && Peek() != '\n')
                {
                    position++;
                }

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
                        if (next is not ('n' or 'r' or 't' or '\\' or '\'' or '"'))
                        {
                            source.Error("AUV1000", $"Unsupported escape '\\{next}'.", new("", position - 2));
                        }

                        next = next switch
                        {
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            _ => next
                        };
                    }
                    value.Append(next);
                }
                if (!closed)
                {
                    source.Error("AUV1000", "Unterminated string.", new("", start));
                }

                tokens.Add(new(source.Text[start..position], start, value.ToString()));
            }
            else
            {
                var pair = c.ToString() + Peek();
                if (pair is ":=" or "==" or "!=" or "<=" or ">=" or "&&" or "||")
                {
                    position++;
                    tokens.Add(new(pair, start));
                }
                else if ("(){}[]:,.=+-*/%!<>;".Contains(c))
                {
                    tokens.Add(new(c.ToString(), start));
                }
                else
                {
                    source.Error("AUV1000", c == '$'
                    ? "Auvil variables have no '$' prefix. Use 'name: string = ...'."
                    : $"Unexpected character '{c}'.", new(c.ToString(), start));
                }
            }
        }
        tokens.Add(new("<eof>", source.Text.Length));
        return tokens;
    }

    private char Peek() => position < source.Text.Length ? source.Text[position] : '\0';
}
