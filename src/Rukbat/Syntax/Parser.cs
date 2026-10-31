using System.Globalization;

namespace Rukbat
{
    // Parse statements by recursive descent and binary expressions by precedence climbing.
    internal sealed class Parser(Source source, List<Token> tokens, FormattingSyntax? formatting = null,
        Dictionary<List<Statement>, (int Start, int End)>? blocks = null,
        Dictionary<ValueType, Token>? typeTokens = null,
        Dictionary<ValueType, Token>? typeQualifiers = null,
        Dictionary<List<Argument>, CallSyntax>? callSites = null)
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

        internal static bool IsReservedWord(string text) => text is
            "fn" or "function" or "func" or "struct" or "impl" or "self" or "class" or
            "return" or "if" or "else" or "import" or "true" or "false" or
            "powershell" or "print" or "eprint" or "args" or "exit" or "for" or "in" or "break" or "continue" or
            "try" or "catch" or "throw" or "null" or "const";
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
                    if (statement is Function or Rukbat.StructDeclaration or ImplDeclaration or Variable)
                    {
                        source.DeclarationStarts[statement] = start;
                    }

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
            Token? qualifier = null;
            if (Match("."))
            {
                qualifier = token;
                token = Identifier();
                name += "." + token.Text;
            }

            var type = new ValueType(name);
            if (name == "dict" && Match("["))
            {
                NewLines();
                var key = Type();
                NewLines();
                Expect(",");
                NewLines();
                var value = Type();
                NewLines();
                Expect("]");
                type = ValueType.DictionaryOf(key, value);
            }
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
            if (qualifier is not null)
            {
                typeQualifiers?.Add(type, qualifier);
            }

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
            while (Current.Text is not ("}" or "<eof>"))
            {
                var start = Current.Offset;
                if (Current.Text == "fn")
                {
                    Fail($"Struct declarations contain only fields. Define methods in 'impl {name.Text} {{ ... }}'.");
                }

                var field = Identifier();
                Expect(":");
                var declaration = new Field(field, Type());
                fields.Add(declaration);
                source.DeclarationStarts[declaration] = start;
                EndStatement();
            }
            Expect("}");
            return new StructDeclaration(name, fields);
        }

        private ImplDeclaration ImplementationDeclaration()
        {
            var name = Identifier();
            NewLines();
            Expect("{");
            Separators();
            var methods = new List<Function>();
            while (Current.Text is not ("}" or "<eof>"))
            {
                var start = Current.Offset;
                if (!Match("fn"))
                {
                    Fail("An impl block contains only method declarations starting with 'fn'.");
                }

                if (methods.Count != 0)
                {
                    formatting?.RequiredBlankLines.Add(start);
                }

                var method = FunctionDeclaration();
                methods.Add(method);
                source.DeclarationStarts[method] = start;
                EndStatement();
            }
            Expect("}");
            return new ImplDeclaration(name, methods);
        }

        private static readonly IReadOnlyDictionary<string, Func<Parser, Token, Statement>> StatementParsers =
            new Dictionary<string, Func<Parser, Token, Statement>>(StringComparer.Ordinal)
            {
                ["import"] = static (parser, token) => parser.ImportDeclaration(token),
                ["struct"] = static (parser, _) => parser.StructDeclaration(),
                ["impl"] = static (parser, _) => parser.ImplementationDeclaration(),
                ["fn"] = static (parser, _) => parser.FunctionDeclaration(),
                ["return"] = static (parser, token) => new Return(token,
                    parser.Current.Text is "\n" or ";" or "}" or "<eof>" ? null : parser.Expression()),
                ["throw"] = static (parser, token) => new Throw(token, parser.Expression()),
                ["try"] = static (parser, token) => parser.TryStatement(token),
                ["for"] = static (parser, _) => parser.ForStatement(),
                ["while"] = static (parser, token) => new While(token, parser.Expression(allowStructLiteral: false), parser.Block()),
                ["break"] = static (_, token) => new LoopControl(token),
                ["continue"] = static (_, token) => new LoopControl(token),
                ["if"] = static (parser, token) => parser.ConditionalStatement(token)
            };

        private TryCatch TryStatement(Token token)
        {
            var body = Block();
            NewLines();
            Expect("catch");
            var name = Identifier();
            return new TryCatch(token, body, new Catch(name, Block()));
        }

        private For ForStatement()
        {
            var item = Identifier();
            Expect("in");
            var items = Expression(allowStructLiteral: false);
            return new For(item, items, Block());
        }

        private Variable VariableDeclaration(bool isConst)
        {
            var name = Identifier();
            var inferred = Take().Text == ":=";
            var type = inferred ? ValueType.Error : Type();
            if (!inferred)
            {
                Expect("=");
            }
            NewLines();
            var value = Expression();
            return new Variable(name, type, value, inferred, isConst) { InitializerEndOffset = Current.Offset };
        }

        private Statement Statement()
        {
            if (StatementParsers.TryGetValue(Current.Text, out var parse))
            {
                return parse(this, Take());
            }

            var isConst = Match("const");
            if (Next.Text is ":" or ":=")
            {
                return VariableDeclaration(isConst);
            }
            if (isConst)
            {
                Fail("A const declaration requires 'name := value' or 'name: Type = value'.");
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
                var aliasToken = Identifier();
                return new Import(token, aliasToken.Text, path) { NameToken = aliasToken };
            }
            var nameToken = Identifier();
            var alias = Match("as") ? Identifier() : nameToken;
            return new Import(token, alias.Text, nameToken.Text, true) { NameToken = alias };
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
            bool ReadType()
            {
                if (!IdentifierAt(index))
                {
                    return false;
                }

                var dictionary = tokens[index].Text == "dict";
                index++;
                if (index < tokens.Count && tokens[index].Text == ".")
                {
                    if (!IdentifierAt(++index))
                    {
                        return false;
                    }

                    index++;
                    dictionary = false;
                }
                if (dictionary && index < tokens.Count && tokens[index].Text == "[")
                {
                    index++;
                    SkipLines();
                    if (!ReadType())
                    {
                        return false;
                    }

                    SkipLines();
                    if (index >= tokens.Count || tokens[index++].Text != ",")
                    {
                        return false;
                    }

                    SkipLines();
                    if (!ReadType())
                    {
                        return false;
                    }

                    SkipLines();
                    if (index >= tokens.Count || tokens[index++].Text != "]")
                    {
                        return false;
                    }
                }
                while (index < tokens.Count)
                {
                    if (tokens[index].Text == "?") { index++; continue; }
                    if (tokens[index].Text == "[" && index + 1 < tokens.Count && tokens[index + 1].Text == "]") { index += 2; continue; }
                    break;
                }
                return true;
            }
            void SkipLines()
            {
                while (index < tokens.Count && tokens[index].Text == "\n")
                {
                    index++;
                }
            }
            return ReadType() && index + 1 < tokens.Count && tokens[index].Text == ">" && tokens[index + 1].Text == "(";
        }

        private Expression Atom(bool allowStructLiteral = true)
        {
            if (Current.Text == "{")
            {
                var opening = Take();
                var entries = new List<DictionaryEntry>();
                NewLines();
                while (Current.Text != "}")
                {
                    var key = Expression();
                    Expect(":");
                    NewLines();
                    entries.Add(new(key, Expression()));
                    NewLines();
                    if (!Match(","))
                    {
                        break;
                    }

                    NewLines();
                }
                var closing = Expect("}");
                formatting?.ExpressionClosings.Add(closing.Offset);
                return new DictionaryLiteral(opening, entries);
            }
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
            var opening = Expect(open);
            var arguments = new List<Argument>();
            var separators = callSites is null ? null : new List<Token>();
            Token? closing = null;
            try
            {
                NewLines();
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
                        if (Current.Text == ",")
                        {
                            separators?.Add(Current);
                        }
                    } while (Match(","));
                }

                if (close == "}")
                {
                    formatting?.ExpressionClosings.Add(Current.Offset);
                }

                closing = Expect(close);
                return arguments;
            }
            finally
            {
                // Retain syntax-only call boundaries even when an editor argument is unfinished.
                callSites?.Add(arguments, new(opening, closing, separators!));
            }
        }
    }
}
