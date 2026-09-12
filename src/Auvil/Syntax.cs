namespace Auvil;

public sealed record Diagnostic(string Code, string Message, int Line, int Column, string FilePath = "<input>");
public sealed record CompilationResult(string? Output, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Output is not null && Diagnostics.Count == 0;
}

internal enum ValueType { Error, Int, String, Bool, Void }
internal sealed record Token(string Text, int Offset, string? StringValue = null);
internal abstract record Node(Token Token);
internal abstract record Statement(Token Token) : Node(Token);
internal abstract record Expression(Token Token) : Node(Token);
internal sealed record Import(Token Token, string Name) : Statement(Token);
internal sealed record Parameter(Token Token, ValueType Type) : Node(Token);
internal sealed record Function(Token Token, string Name, List<Parameter> Parameters, ValueType Type, List<Statement> Body) : Statement(Token);
internal sealed record Variable(Token Token, ValueType Type, Expression Value) : Statement(Token);
internal sealed record Assignment(Token Token, Expression Value) : Statement(Token);
internal sealed record Return(Token Token, Expression? Value) : Statement(Token);
internal sealed record Conditional(Token Token, Expression Condition, List<Statement> Then, List<Statement> Else) : Statement(Token);
internal sealed record ExpressionStatement(Expression Value) : Statement(Value.Token);
internal sealed record Literal(Token Token, object Value) : Expression(Token);
internal sealed record Name(Token Token) : Expression(Token);
internal sealed record Unary(Token Token, Expression Value) : Expression(Token);
internal sealed record Binary(Token Token, Expression Left, Expression Right) : Expression(Token);
internal sealed record Argument(Token Token, string? Name, Expression Value) : Node(Token);
internal sealed record Call(Token Token, string? Module, string Name, List<Argument> Arguments, ValueType? PowerShellType = null) : Expression(Token);

internal sealed class Source(string text, string path)
{
    public string Text { get; } = text;
    public string Path { get; } = path;
    public List<Diagnostic> Diagnostics { get; } = [];
    public void Error(string code, string message, Token token)
    {
        var prefix = Text[..Math.Clamp(token.Offset, 0, Text.Length)];
        Diagnostics.Add(new(code, message, prefix.Count(c => c == '\n') + 1,
            prefix.Length - prefix.LastIndexOf('\n'), Path));
    }
}

internal sealed class Module(Source source, string id, List<Statement> statements)
{
    public Source Source { get; } = source;
    public string Id { get; } = id;
    public List<Statement> Statements { get; } = statements;
    public Dictionary<string, Module> Imports { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Function> Functions { get; } = new(StringComparer.Ordinal);
}
