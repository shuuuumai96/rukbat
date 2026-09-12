namespace Auvil;

// Recursive descent for statements, precedence climbing for expressions.
internal sealed class Parser(Source source, List<Token> tokens)
{
    private sealed class ParseFailure : Exception;
    private int position;
    private Token Current => tokens[Math.Min(position, tokens.Count - 1)];
    private Token Next => tokens[Math.Min(position + 1, tokens.Count - 1)];
    private Token Take() { var token = Current; position++; return token; }
    private bool Match(string text) { if (Current.Text != text) return false; Take(); return true; }
    private Token Expect(string text) => Current.Text == text ? Take() : Fail($"Expected '{text}', got '{Current.Text}'.");
    private Token Fail(string message) { source.Error("AUV1000", message, Current); throw new ParseFailure(); }
    private void NewLines() { while (Match("\n")) { } }
    private void Separators() { while (Current.Text is "\n" or ";") Take(); }
    private Token Identifier()
    {
        var token = Current;
        if (token.StringValue is not null || token.Text.Length == 0 || !(char.IsLetter(token.Text[0]) || token.Text[0] == '_') ||
            token.Text is "function" or "return" or "if" or "else" or "import" or "true" or "false" or "powershell" or "print")
            return Fail("Expected an identifier (reserved words cannot be used as names).");
        return Take();
    }
    public List<Statement> Parse()
    {
        var statements = new List<Statement>();
        try { Separators(); while (Current.Text != "<eof>") { statements.Add(Statement()); EndStatement(); } }
        catch (ParseFailure) { }
        return statements;
    }
    private void EndStatement()
    {
        if (Current.Text is not ("\n" or ";" or "}" or "<eof>")) Fail("Expected a newline or ';' between statements.");
        Separators();
    }
    private List<Statement> Block()
    {
        NewLines(); Expect("{"); Separators();
        var statements = new List<Statement>();
        while (Current.Text is not ("}" or "<eof>")) { statements.Add(Statement()); EndStatement(); }
        Expect("}");
        return statements;
    }
    private ValueType Type()
    {
        var token = Take();
        var type = token.Text switch { "int" => ValueType.Int, "string" => ValueType.String, "bool" => ValueType.Bool, "void" => ValueType.Void, _ => ValueType.Error };
        if (type == ValueType.Error) source.Error("AUV1001", $"Unsupported type '{token.Text}'.", token);
        return type;
    }
    private Statement Statement()
    {
        var token = Current;
        if (Match("import")) return new Import(token, Identifier().Text);
        if (Match("function"))
        {
            var name = Identifier(); Expect("("); NewLines();
            var parameters = new List<Parameter>();
            if (Current.Text != ")")
                do { NewLines(); var parameter = Identifier(); Expect(":"); parameters.Add(new(parameter, Type())); NewLines(); } while (Match(","));
            Expect(")"); Expect(":"); var type = Type();
            return new Function(name, name.Text, parameters, type, Block());
        }
        if (Match("return")) return new Return(token, Current.Text is "\n" or ";" or "}" or "<eof>" ? null : Expression());
        if (Match("if"))
        {
            var condition = Expression(); var then = Block();
            var saved = position; NewLines();
            if (Match("else")) return new Conditional(token, condition, then, Block());
            position = saved;
            return new Conditional(token, condition, then, []);
        }
        if (Next.Text == ":")
        {
            var name = Identifier(); Expect(":"); var type = Type(); Expect("="); NewLines();
            return new Variable(name, type, Expression());
        }
        if (Next.Text == "=") { var name = Identifier(); Expect("="); NewLines(); return new Assignment(name, Expression()); }
        return new ExpressionStatement(Expression());
    }
    private static int Precedence(string op) => op switch
    {
        "||" => 1, "&&" => 2, "==" or "!=" => 3, "<" or "<=" or ">" or ">=" => 4,
        "+" or "-" => 5, "*" or "/" or "%" => 6, _ => 0
    };
    private Expression Expression(int minimum = 1)
    {
        var left = Primary();
        while (Precedence(Current.Text) >= minimum)
        {
            var op = Take(); NewLines();
            left = new Binary(op, left, Expression(Precedence(op.Text) + 1));
        }
        return left;
    }
    private Expression Primary()
    {
        var token = Current;
        if (Current.Text is "-" or "+" or "!")
        {
            Take();
            // The minimum signed integer has a magnitude outside Int32's positive range.
            if (token.Text == "-" && Current.Text == "2147483648") { Take(); return new Literal(token, int.MinValue); }
            return new Unary(token, Primary());
        }
        if (Match("(")) { NewLines(); var expression = Expression(); NewLines(); Expect(")"); return expression; }
        if (token.StringValue is not null) { Take(); return new Literal(token, token.StringValue); }
        if (token.Text is "true" or "false") { Take(); return new Literal(token, token.Text == "true"); }
        if (char.IsAsciiDigit(token.Text[0]))
        {
            Take();
            if (!int.TryParse(token.Text, out var value)) source.Error("AUV1001", "Integer literal is outside Int32 range.", token);
            return new Literal(token, value);
        }
        if (Match("powershell"))
        {
            Expect("<"); var type = Type(); Expect(">");
            return new Call(token, null, "powershell", Arguments(), type);
        }
        if (Match("print")) return new Call(token, null, "print", Arguments());
        var name = Identifier();
        if (Match(".")) return new Call(name, name.Text, Identifier().Text, Arguments());
        return Current.Text == "(" ? new Call(name, null, name.Text, Arguments()) : new Name(name);
    }
    private List<Argument> Arguments()
    {
        Expect("("); NewLines(); var arguments = new List<Argument>();
        if (Current.Text != ")")
            do
            {
                NewLines(); var token = Current; string? name = null;
                if (Next.Text == ":") { name = Identifier().Text; Expect(":"); }
                arguments.Add(new(token, name, Expression())); NewLines();
            } while (Match(","));
        Expect(")"); return arguments;
    }
}
