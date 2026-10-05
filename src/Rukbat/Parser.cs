using System.Globalization;

namespace Rukbat;

// Parse statements by recursive descent and binary expressions by precedence climbing.
internal sealed class Parser(Source source, List<Token> tokens, FormattingSyntax? formatting = null,
    Dictionary<List<Statement>, (int Start, int End)>? blocks = null,
    Dictionary<ValueType, Token>? typeTokens = null)
{
    private sealed class ParseFailure : Exception;
    private int position;
    private Token Current => tokens[Math.Min(position, tokens.Count - 1)];
    private Token Next => tokens[Math.Min(position + 1, tokens.Count - 1)];
    private Token Take()
    {
        var token = Current;
        position++;
        return token;
    }

    private bool Match(string text)
    {
        if (Current.Text != text)
        {
            return false;
        }

        Take();
        return true;
    }

    private Token Expect(string text) => Current.Text == text ? Take() : Fail($"Expected '{text}', got '{Current.Text}'.");
    private Token Fail(string message)
    {
        source.Error("RUK1000", message, Current);
        throw new ParseFailure();
    }

    private void NewLines()
    {
        while (Current.Text == "\n")
        {
            var newline = Take();
            formatting?.ContinuationNewlines.Add(newline.Offset);
        }
    }

    private void Separators()
    {
        while (Current.Text is "\n" or ";")
        {
            formatting?.ContinuationNewlines.Remove(Current.Offset);
            Take();
        }
    }

    private Token Identifier()
    {
        var token = Current;
        if (token.StringValue is not null ||
            token.Text.Length == 0 ||
            !(char.IsLetter(token.Text[0]) || token.Text[0] == '_') ||
            IsReservedWord(token.Text))
        {
            return Fail("Expected an identifier (reserved words cannot be used as names).");
        }

        return Take();
    }

    private static bool IsReservedWord(string text) => text is
        "fn" or "function" or "func" or "struct" or "self" or "class" or
        "return" or "if" or "else" or "import" or "true" or "false" or
        "powershell" or "print" or "eprint" or "args" or "exit" or "for" or "in" or "break" or "continue" or
        "try" or "catch" or "throw" or "null";
    public List<Statement> Parse()
    {
        var statements = new List<Statement>();
        try
        {
            Separators();
            while (Current.Text != "<eof>")
            {
                var start = Current.Offset;
                var statement = Statement();
                if (statement is Function or Rukbat.StructDeclaration or Variable)
                    source.DeclarationStarts[statement] = start;
                formatting?.SeparateStatements(statements.LastOrDefault(), statement, start);
                statements.Add(statement);
                EndStatement();
            }
        }
        catch (ParseFailure)
        {
            // Fail has already recorded the diagnostic. Stop at the first syntax error.
        }
        return statements;
    }

    private void EndStatement()
    {
        if (Current.Text is not ("\n" or ";" or "}" or "<eof>"))
        {
            Fail("Expected a newline or ';' between statements.");
        }

        Separators();
    }

    private List<Statement> Block()
    {
        NewLines();
        var opening = Expect("{");
        Separators();
        var statements = new List<Statement>();
        while (Current.Text is not ("}" or "<eof>"))
        {
            var start = Current.Offset;
            var statement = Statement();
            formatting?.SeparateStatements(statements.LastOrDefault(), statement, start);
            statements.Add(statement);
            EndStatement();
        }
        var closing = Expect("}");
        blocks?.Add(statements, (opening.Offset, closing.Offset + 1));
        return statements;
    }

    private ValueType Type()
    {
        var token = Identifier();
        var name = token.Text;
        if (Match("."))
        {
            token = Identifier();
            name += "." + token.Text;
        }

        var type = new ValueType(name);
        while (Current.Text is "[" or "?")
        {
            if (Match("?"))
            {
                type = ValueType.NullableOf(type);
            }
            else
            {
                Expect("[");
                Expect("]");
                type = ValueType.ArrayOf(type);
            }
        }
        typeTokens?.Add(type, token);
        return type;
    }

    private Function FunctionDeclaration()
    {
        var name = Identifier();
        Expect("(");
        NewLines();
        var parameters = new List<Parameter>();
        if (Current.Text != ")")
        {
            do
            {
                NewLines();
                var parameter = Identifier();
                Expect(":");
                parameters.Add(new(parameter, Type()));
                NewLines();
            } while (Match(","));
        }

        Expect(")");
        Expect(":");
        var type = Type();
        return new Function(name, name.Text, parameters, type, Block());
    }

    private StructDeclaration StructDeclaration()
    {
        var name = Identifier();
        NewLines();
        Expect("{");
        Separators();
        var fields = new List<Field>();
        var methods = new List<Function>();
        while (Current.Text is not ("}" or "<eof>"))
        {
            var start = Current.Offset;
            if (Match("fn"))
            {
                if (fields.Count != 0 || methods.Count != 0)
                {
                    formatting?.RequiredBlankLines.Add(start);
                }
                var method = FunctionDeclaration();
                methods.Add(method);
                source.DeclarationStarts[method] = start;
            }
            else
            {
                if (methods.Count != 0)
                {
                    formatting?.RequiredBlankLines.Add(start);
                }
                var field = Identifier();
                Expect(":");
                var declaration = new Field(field, Type());
                fields.Add(declaration);
                source.DeclarationStarts[declaration] = start;
            }
            EndStatement();
        }
        Expect("}");
        return new StructDeclaration(name, fields, methods);
    }

    private Statement Statement()
    {
        var token = Current;
        if (Match("import"))
        {
            return ImportDeclaration(token);
        }

        if (Match("struct"))
        {
            return StructDeclaration();
        }

        if (Match("fn"))
        {
            return FunctionDeclaration();
        }

        if (Match("return"))
        {
            return new Return(token, Current.Text is "\n" or ";" or "}" or "<eof>" ? null : Expression());
        }

        if (Match("throw"))
        {
            return new Throw(token, Expression());
        }
        if (Match("try"))
        {
            var body = Block();
            NewLines();
            Expect("catch");
            var name = Identifier();
            return new TryCatch(token, body, new Catch(name, Block()));
        }

        if (Match("for"))
        {
            var item = Identifier();
            Expect("in");
            var items = Expression(allowStructLiteral: false);
            return new For(item, items, Block());
        }
        if (Match("while"))
        {
            return new While(token, Expression(allowStructLiteral: false), Block());
        }
        if (Match("break") || Match("continue"))
        {
            return new LoopControl(token);
        }

        if (Match("if"))
        {
            return ConditionalStatement(token);
        }
        if (Next.Text == ":")
        {
            var name = Identifier();
            Expect(":");
            var type = Type();
            Expect("=");
            NewLines();
            return new Variable(name, type, Expression());
        }
        if (Next.Text == ":=")
        {
            var name = Identifier();
            Expect(":=");
            NewLines();
            return new Variable(name, ValueType.Error, Expression(), true);
        }
        if (Next.Text == "=")
        {
            var name = Identifier();
            Expect("=");
            NewLines();
            return new Assignment(name, Expression());
        }
        var expression = Expression();
        if (Match("="))
        {
            NewLines();
            if (expression is Member member)
            {
                return new FieldAssignment(member, Expression());
            }

            if (expression is Index index)
            {
                return new IndexAssignment(index, Expression());
            }

            Fail("Field assignment requires a field on a variable.");
        }
        return new ExpressionStatement(expression);
    }

    private Conditional ConditionalStatement(Token token)
    {
        var condition = Expression(allowStructLiteral: false);
        var then = Block();
        var endOfThen = position;

        // Allow else on the next line, but restore the statement separator if it is absent.
        NewLines();
        if (Match("else"))
        {
            return new Conditional(token, condition, then, Block());
        }

        position = endOfThen;
        return new Conditional(token, condition, then, []);
    }

    private Import ImportDeclaration(Token token)
    {
        if (Current.StringValue is { } path)
        {
            Take();
            Expect("as");
            return new Import(token, Identifier().Text, path);
        }
        var name = Identifier().Text;
        var alias = Match("as") ? Identifier().Text : name;
        return new Import(token, alias, name, true);
    }

    private static int Precedence(string op) => op switch
    {
        "||" => 1,
        "&&" => 2,
        "==" or "!=" => 3,
        "<" or "<=" or ">" or ">=" => 4,
        "+" or "-" => 5,
        "*" or "/" or "%" => 6,
        _ => 0
    };

    private Expression Expression(bool allowStructLiteral = true, int minimum = 1)
    {
        var left = Primary(allowStructLiteral);
        while (Precedence(Current.Text) >= minimum)
        {
            var op = Take();
            NewLines();
            // Consume only stronger operators on the right, keeping equal precedence left-associative.
            left = new Binary(op, left, Expression(allowStructLiteral, Precedence(op.Text) + 1));
        }
        return left;
    }

    private Expression Primary(bool allowStructLiteral = true)
    {
        var expression = Atom(allowStructLiteral);
        while (Current.Text is "." or "[")
        {
            if (Match("."))
            {
                var member = Identifier();
                if (Current.Text == "<" && HasTypeArgumentCall())
                {
                    var delimiter = Take();
                    formatting?.GenericDelimiters.Add(delimiter.Offset);
                    var typeArgument = Type();
                    formatting?.GenericDelimiters.Add(Current.Offset);
                    Expect(">");
                    expression = new Call(member, expression, member.Text, Arguments(), TypeArgument: typeArgument);
                }
                else
                {
                    expression = Current.Text == "(" ? new Call(member, expression, member.Text, Arguments())
                        : allowStructLiteral && Current.Text == "{" ? new Call(member, expression, member.Text, StructArguments(), IsStructLiteral: true)
                        : new Member(member, expression, member.Text);
                }
            }
            else
            {
                var bracket = Take();
                NewLines();
                var offset = Expression();
                NewLines();
                Expect("]");
                expression = new Index(bracket, expression, offset);
            }
        }
        return expression;
    }

    private bool HasTypeArgumentCall()
    {
        // A member followed by '<' may be a comparison. Only a complete type
        // argument followed by '(' selects the typed-call syntax.
        var index = position + 1;
        bool IdentifierAt(int offset) => offset < tokens.Count && tokens[offset].StringValue is null && tokens[offset].Text.Length != 0 &&
            (char.IsLetter(tokens[offset].Text[0]) || tokens[offset].Text[0] == '_');
        if (!IdentifierAt(index)) return false;
        index++;
        if (index < tokens.Count && tokens[index].Text == ".")
        {
            if (!IdentifierAt(++index)) return false;
            index++;
        }
        while (index < tokens.Count)
        {
            if (tokens[index].Text == "?") { index++; continue; }
            if (tokens[index].Text == "[" && index + 1 < tokens.Count && tokens[index + 1].Text == "]") { index += 2; continue; }
            break;
        }
        return index + 1 < tokens.Count && tokens[index].Text == ">" && tokens[index + 1].Text == "(";
    }

    private Expression Atom(bool allowStructLiteral = true)
    {
        var token = Current;
        if (Current.Text is "-" or "+" or "!")
        {
            formatting?.UnaryOperators.Add(token.Offset);
            Take();
            // The magnitude of long.MinValue exceeds the positive range, so consume its sign here.
            if (token.Text == "-" && Current.Text == "9223372036854775808")
            {
                Take();
                return new Literal(token, long.MinValue);
            }
            return new Unary(token, Primary(allowStructLiteral));
        }
        if (Match("["))
        {
            formatting?.ExpressionOpenings.Add(token.Offset);
            NewLines();
            var items = new List<Expression>();
            while (Current.Text != "]")
            {
                items.Add(Expression());
                NewLines();
                if (!Match(","))
                {
                    break;
                }

                NewLines();
            }
            Expect("]");
            return new ArrayLiteral(token, items);
        }
        if (Match("("))
        {
            formatting?.ExpressionOpenings.Add(token.Offset);
            NewLines();
            var expression = Expression();
            NewLines();
            Expect(")");
            return expression;
        }
        if (token.StringValue is not null)
        {
            Take();
            return new Literal(token, token.StringValue);
        }
        if (token.Text is "true" or "false")
        {
            Take();
            return new Literal(token, token.Text == "true");
        }
        if (Match("null"))
        {
            return new Literal(token, NullValue.Instance);
        }
        if (char.IsAsciiDigit(token.Text[0]))
        {
            Take();
            if (token.Text.Contains('.') || token.Text.Contains('e', StringComparison.OrdinalIgnoreCase))
            {
                if (!double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue) ||
                    !double.IsFinite(floatValue) ||
                    floatValue == 0 && token.Text.Split('e', 'E')[0].Any(c => c is >= '1' and <= '9'))
                {
                    source.Error("RUK1001", "Float literal must be a finite IEEE 754 binary64 value.", token);
                }

                return new Literal(token, floatValue);
            }
            if (!long.TryParse(token.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                source.Error("RUK1001", "Integer literal is outside Int64 range.", token);
            }

            return new Literal(token, value);
        }
        if (Match("powershell"))
        {
            formatting?.GenericDelimiters.Add(Current.Offset);
            Expect("<");
            var type = Type();
            formatting?.GenericDelimiters.Add(Current.Offset);
            Expect(">");
            return new Call(token, null, "powershell", Arguments(), type);
        }
        if (Match("print") || Match("eprint") || Match("args") || Match("exit"))
        {
            return new Call(token, null, token.Text, Arguments());
        }

        if (Match("self"))
        {
            return new Name(token);
        }

        var name = Identifier();
        return Current.Text == "(" ? new Call(name, null, name.Text, Arguments())
            : allowStructLiteral && Current.Text == "{" ? new Call(name, null, name.Text, StructArguments(), IsStructLiteral: true)
            : new Name(name);
    }

    private List<Argument> Arguments()
        => Arguments("(", ")");

    private List<Argument> StructArguments()
        => Arguments("{", "}");

    private List<Argument> Arguments(string open, string close)
    {
        Expect(open);
        NewLines();
        var arguments = new List<Argument>();
        if (Current.Text != close)
        {
            do
            {
                NewLines();
                var token = Current;
                string? name = null;
                if (Next.Text == ":")
                {
                    name = Identifier().Text;
                    Expect(":");
                }
                arguments.Add(new(token, name, Expression()));
                NewLines();
            } while (Match(","));
        }

        if (close == "}")
        {
            formatting?.ExpressionClosings.Add(Current.Offset);
        }
        Expect(close);
        return arguments;
    }
}
