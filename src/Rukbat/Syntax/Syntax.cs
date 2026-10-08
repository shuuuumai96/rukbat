namespace Rukbat;

internal sealed class NullValue
{
    public static readonly NullValue Instance = new();
    private NullValue() { }
}
internal sealed record Token(string Text, int Offset, string? StringValue = null);
internal sealed record CallSyntax(Token Opening, Token? Closing, IReadOnlyList<Token> Separators);

internal abstract record Node(Token Token);
internal abstract record Statement(Token Token) : Node(Token);
internal abstract record Expression(Token Token) : Node(Token);
internal sealed record Import(Token Token, string Name, string Path, bool IsStandard = false) : Statement(Token)
{
    public Token NameToken { get; init; } = Token;
}
internal sealed record Parameter(Token Token, ValueType Type) : Node(Token);
internal sealed record Function(
    Token Token,
    string Name,
    List<Parameter> Parameters,
    ValueType Type,
    List<Statement> Body) : Statement(Token);

internal sealed record Variable(Token Token, ValueType Type, Expression Value, bool IsInferred = false, bool IsConst = false) : Statement(Token);
internal sealed record Assignment(Token Token, Expression Value) : Statement(Token);
internal sealed record FieldAssignment(Member Target, Expression Value) : Statement(Target.Token);
internal sealed record Field(Token Token, ValueType Type) : Node(Token);
internal sealed record StructDeclaration(Token Token, List<Field> Fields, List<Function> Methods) : Statement(Token);
internal sealed record Member(Token Token, Expression Receiver, string Name) : Expression(Token);
internal sealed record ArrayLiteral(Token Token, List<Expression> Items) : Expression(Token);
internal sealed record DictionaryEntry(Expression Key, Expression Value);
internal sealed record DictionaryLiteral(Token Token, List<DictionaryEntry> Entries) : Expression(Token);
internal sealed record Index(Token Token, Expression Receiver, Expression Offset) : Expression(Token);
internal sealed record IndexAssignment(Index Target, Expression Value) : Statement(Target.Token);
internal sealed record For(Token Token, Expression Items, List<Statement> Body) : Statement(Token);
internal sealed record While(Token Token, Expression Condition, List<Statement> Body) : Statement(Token);
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
    ValueType? PowerShellType = null,
    ValueType? TypeArgument = null,
    bool IsStructLiteral = false) : Expression(Token);
