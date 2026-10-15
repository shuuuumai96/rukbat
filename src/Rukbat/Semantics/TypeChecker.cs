namespace Rukbat;

internal sealed partial class TypeChecker
{
    public Dictionary<Node, VariableSymbol> Variables { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Function, FunctionSymbol> Functions { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Call, FunctionSymbol> Calls { get; } = new(ReferenceEqualityComparer.Instance);
    public HashSet<Call> ArrayAppends { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Call, string> StandardCalls { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Expression, ValueType> Types { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<For, BoundIteration> Iterations { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Member, DateTimeValues.ZoneConstant> ZoneConstants { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Member, string> HashConstants { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Member, HttpConstant> HttpConstants { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Member, StandardEncoding> EncodingConstants { get; } = new(ReferenceEqualityComparer.Instance);
    private Dictionary<VariableSymbol, ValueType> refinements = [];
    private int variableId;
    private int loopDepth;
    private Module module = null!;

    public void Check(List<Module> modules)
    {
        RegisterDefinitions(modules);
        foreach (var current in modules)
        {
            module = current;
            var globals = new Symbols();
            var statements = current.Statements
                .Where(statement => statement is not Function and not StructDeclaration)
                .ToList();
            CheckStatements(statements, globals, null, true);
            foreach (var symbol in Functions.Values.Where(f => f.Module == current))
            {
                refinements.Clear();
                var function = symbol.Function;
                if (symbol.Owner is null && current.Imports.ContainsKey(function.Name))
                {
                    Error("RUK2002", $"Name '{function.Name}' is already an import.", function);
                }

                // Functions capture their defining module's scope, never the caller's locals.
                var locals = new Symbols(globals);
                if (symbol.Owner is { } owner)
                {
                    locals.Variables.Add("self", new("self", owner.BoundType, "$__rukbat_self"));
                }

                foreach (var parameter in function.Parameters)
                {
                    Declare(parameter, DeclaredTypes[parameter], locals, false);
                }

                CheckStatements(function.Body, locals, symbol.ReturnType, false);
                if (symbol.ReturnType != ValueType.Void && !AlwaysExits(function.Body))
                {
                    Error("RUK2003", $"Function '{function.Name}' must return a value or throw on every path.", function);
                }
            }
        }
        CheckConstMethodCalls(modules);
    }

    private void Error(string code, string message, Node node) => module.Source.Error(code, message, node.Token);
}
