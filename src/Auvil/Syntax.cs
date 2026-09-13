namespace Auvil;

public sealed record Diagnostic(
    string Code,
    string Message,
    int Line,
    int Column,
    string FilePath = "<input>");

public sealed record CompilationResult(string? Output, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Output is not null && Diagnostics.Count == 0;
}

// The AST retains type names until the checker binds them to their defining StructSymbol.
// Structs defined in different modules remain distinct even when their fields match.
internal sealed record ValueType(string Name, StructSymbol? Struct = null, ValueType? Element = null)
{
    public static readonly ValueType Error = new("error");
    public static readonly ValueType Int = new("int");
    public static readonly ValueType String = new("string");
    public static readonly ValueType Bool = new("bool");
    public static readonly ValueType Void = new("void");
    public static ValueType ArrayOf(ValueType element) => new(element.Name + "[]", Element: element);
    public bool IsComposite => Struct is not null || Element is not null;
    public override string ToString() => Name;
}
internal sealed record Token(string Text, int Offset, string? StringValue = null);
internal abstract record Node(Token Token);
internal abstract record Statement(Token Token) : Node(Token);
internal abstract record Expression(Token Token) : Node(Token);
internal sealed record Import(Token Token, string Name, string Path) : Statement(Token);
internal sealed record Parameter(Token Token, ValueType Type) : Node(Token);
internal sealed record Function(
    Token Token,
    string Name,
    List<Parameter> Parameters,
    ValueType Type,
    List<Statement> Body) : Statement(Token);

internal sealed record Variable(Token Token, ValueType Type, Expression Value, bool IsInferred = false) : Statement(Token);
internal sealed record Assignment(Token Token, Expression Value) : Statement(Token);
internal sealed record FieldAssignment(Member Target, Expression Value) : Statement(Target.Token);
internal sealed record Field(Token Token, ValueType Type) : Node(Token);
internal sealed record StructDeclaration(Token Token, List<Field> Fields, List<Function> Methods) : Statement(Token);
internal sealed record Member(Token Token, Expression Receiver, string Name) : Expression(Token);
internal sealed record ArrayLiteral(Token Token, List<Expression> Items) : Expression(Token);
internal sealed record Index(Token Token, Expression Receiver, Expression Offset) : Expression(Token);
internal sealed record IndexAssignment(Index Target, Expression Value) : Statement(Target.Token);
internal sealed record For(Token Token, Expression Items, List<Statement> Body) : Statement(Token);
internal sealed record LoopControl(Token Token) : Statement(Token);
internal sealed record Return(Token Token, Expression? Value) : Statement(Token);
internal sealed record Throw(Token Token, Expression Value) : Statement(Token);
internal sealed record Catch(Token Token, List<Statement> Body) : Node(Token);
internal sealed record TryCatch(Token Token, List<Statement> Body, Catch Handler) : Statement(Token);
internal sealed record Conditional(
    Token Token,
    Expression Condition,
    List<Statement> Then,
    List<Statement> Else) : Statement(Token);

internal sealed record ExpressionStatement(Expression Value) : Statement(Value.Token);
internal sealed record Literal(Token Token, object Value) : Expression(Token);
internal sealed record Name(Token Token) : Expression(Token);
internal sealed record Unary(Token Token, Expression Value) : Expression(Token);
internal sealed record Binary(Token Token, Expression Left, Expression Right) : Expression(Token);
internal sealed record Argument(Token Token, string? Name, Expression Value) : Node(Token);
internal sealed record Call(
    Token Token,
    Expression? Receiver,
    string Name,
    List<Argument> Arguments,
    ValueType? PowerShellType = null) : Expression(Token);

internal sealed class Source(string text, string path)
{
    public string Text { get; } = text;
    public string Path { get; } = path;
    public List<Diagnostic> Diagnostics { get; } = [];
    public void Error(string code, string message, Token token)
    {
        var prefix = Text[..Math.Clamp(token.Offset, 0, Text.Length)];
        var line = prefix.Count(character => character == '\n') + 1;
        var column = prefix.Length - prefix.LastIndexOf('\n');
        Diagnostics.Add(new(code, message, line, column, Path));
    }
}

internal sealed class Module(Source source, string id, List<Statement> statements)
{
    public Source Source { get; } = source;
    public string Id { get; } = id;
    public List<Statement> Statements { get; } = statements;
    public Dictionary<string, Module> Imports { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, StructSymbol> Structs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Function> Functions { get; } = new(StringComparer.Ordinal);
}
