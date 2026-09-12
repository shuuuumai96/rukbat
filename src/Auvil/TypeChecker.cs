namespace Auvil;

internal sealed record VariableSymbol(string Name, ValueType Type, string PowerShellName);
internal sealed record FunctionSymbol(Module Module, Function Function, string PowerShellName);
internal sealed class Symbols(Symbols? parent = null)
{
    public Dictionary<string, VariableSymbol> Variables { get; } = new(StringComparer.Ordinal);
    public VariableSymbol? Find(string name) => Variables.GetValueOrDefault(name) ?? parent?.Find(name);
}

internal sealed class TypeChecker
{
    public Dictionary<Node, VariableSymbol> Variables { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Function, FunctionSymbol> Functions { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Call, FunctionSymbol> Calls { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Expression, ValueType> Types { get; } = new(ReferenceEqualityComparer.Instance);
    private int variableId;
    private Module module = null!;

    public void Check(List<Module> modules)
    {
        foreach (var current in modules)
        {
            foreach (var function in current.Statements.OfType<Function>())
            {
                if (!current.Functions.TryAdd(function.Name, function))
                    current.Source.Error("AUV2002", $"Duplicate function '{function.Name}'.", function.Token);
                Functions[function] = new(current, function, $"__auv_f{Functions.Count}");
            }
        }
        foreach (var current in modules)
        {
            module = current;
            var globals = new Symbols();
            CheckStatements(current.Statements.Where(s => s is not Function).ToList(), globals, null, true);
            foreach (var function in current.Statements.OfType<Function>())
            {
                if (current.Imports.ContainsKey(function.Name)) Error("AUV2002", $"Name '{function.Name}' is already an import.", function);
                var locals = new Symbols(globals);
                foreach (var parameter in function.Parameters) Declare(parameter, parameter.Type, locals, false);
                CheckStatements(function.Body, locals, function.Type, false);
                if (function.Type != ValueType.Void && !AlwaysReturns(function.Body))
                    Error("AUV2003", $"Function '{function.Name}' must return a value on every path.", function);
            }
        }
    }
    private static bool AlwaysReturns(List<Statement> body) => body.Any(s => s is Return ||
        s is Conditional condition && AlwaysReturns(condition.Then) && AlwaysReturns(condition.Else));

    private void Declare(Node node, ValueType type, Symbols scope, bool global)
    {
        if (type == ValueType.Void) Error("AUV2001", "Variables and parameters cannot have type void.", node);
        var name = node.Token.Text;
        if (module.Functions.ContainsKey(name) || module.Imports.ContainsKey(name))
            Error("AUV2002", $"Name '{name}' is already a function or import.", node);
        var symbol = new VariableSymbol(name, type, $"${(global ? "script:" : "")}__auv_v{variableId++}");
        if (!scope.Variables.TryAdd(name, symbol)) Error("AUV2002", $"Duplicate declaration '{name}'.", node);
        Variables[node] = symbol;
    }
    private void CheckStatements(List<Statement> statements, Symbols scope, ValueType? returnType, bool topLevel)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case Import import when !topLevel: Error("AUV3000", "Imports are allowed only at module level.", import); break;
                case Function function: Error("AUV1002", "Nested functions are not supported yet.", function); break;
                case Variable variable:
                    var actual = TypeOf(variable.Value, scope);
                    Declare(variable, variable.Type, scope, topLevel);
                    Match(variable.Type, actual, variable.Value);
                    break;
                case Assignment assignment:
                    var target = scope.Find(assignment.Token.Text);
                    if (target is null) Error("AUV2004", $"Unknown variable '{assignment.Token.Text}'. Declare it with a type first.", assignment);
                    else { Variables[assignment] = target; Match(target.Type, TypeOf(assignment.Value, scope), assignment.Value); }
                    break;
                case Return result:
                    var resultType = result.Value is null ? ValueType.Void : TypeOf(result.Value, scope);
                    if (returnType is null) Error("AUV2003", "return is allowed only inside a function.", result);
                    else Match(returnType.Value, resultType, result);
                    break;
                case Conditional conditional:
                    Match(ValueType.Bool, TypeOf(conditional.Condition, scope), conditional.Condition);
                    CheckStatements(conditional.Then, new Symbols(scope), returnType, false);
                    CheckStatements(conditional.Else, new Symbols(scope), returnType, false);
                    break;
                case ExpressionStatement expression: TypeOf(expression.Value, scope); break;
            }
        }
    }
    private ValueType TypeOf(Expression expression, Symbols scope)
    {
        var type = expression switch
        {
            Literal literal => literal.Value switch { int => ValueType.Int, string => ValueType.String, bool => ValueType.Bool, _ => ValueType.Error },
            Name name => Resolve(name, scope),
            Unary unary => UnaryType(unary, scope),
            Binary binary => BinaryType(binary, scope),
            Call call => CallType(call, scope),
            _ => ValueType.Error
        };
        Types[expression] = type;
        return type;
    }
    private ValueType Resolve(Name name, Symbols scope)
    {
        var symbol = scope.Find(name.Token.Text);
        if (symbol is null) { Error("AUV2004", $"Unknown variable '{name.Token.Text}'.", name); return ValueType.Error; }
        Variables[name] = symbol;
        return symbol.Type;
    }
    private ValueType UnaryType(Unary unary, Symbols scope)
    {
        var expected = unary.Token.Text == "!" ? ValueType.Bool : ValueType.Int;
        Match(expected, TypeOf(unary.Value, scope), unary);
        return expected;
    }
    private ValueType BinaryType(Binary binary, Symbols scope)
    {
        var left = TypeOf(binary.Left, scope); var right = TypeOf(binary.Right, scope);
        if (left == ValueType.Error || right == ValueType.Error) return ValueType.Error;
        var op = binary.Token.Text;
        if (left == right && left != ValueType.Void && op is "==" or "!=") return ValueType.Bool;
        if (left == ValueType.Bool && right == ValueType.Bool && op is "&&" or "||") return ValueType.Bool;
        if (left == ValueType.Int && right == ValueType.Int)
            return op is "<" or "<=" or ">" or ">=" ? ValueType.Bool :
                op is "+" or "-" or "*" or "/" or "%" ? ValueType.Int : Invalid();
        if (left == ValueType.String && right == ValueType.String && op == "+") return ValueType.String;
        return Invalid();
        ValueType Invalid() { Error("AUV2001", $"Operator '{op}' cannot combine {left} and {right}.", binary); return ValueType.Error; }
    }
    private ValueType CallType(Call call, Symbols scope)
    {
        var arguments = call.Arguments.Select(a => TypeOf(a.Value, scope)).ToArray();
        if (call.PowerShellType is { } powerShellType)
        {
            if (arguments.Length == 0 || call.Arguments[0].Name is not null)
                Error("AUV2005", "powershell<T> requires a command name as its first positional argument.", call);
            else Match(ValueType.String, arguments[0], call.Arguments[0]);
            var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sawNamed = false;
            for (var i = 0; i < call.Arguments.Count; i++)
            {
                var argument = call.Arguments[i];
                if (arguments[i] == ValueType.Void) Error("AUV2001", "A void result cannot be passed as an argument.", argument);
                if (argument.Name is { } name)
                {
                    sawNamed = true;
                    if (!named.Add(name)) Error("AUV2005", $"Duplicate PowerShell parameter '{name}'.", argument);
                }
                else if (sawNamed) Error("AUV2005", "Positional arguments must precede named arguments.", argument);
            }
            return powerShellType;
        }
        if (call.Module is null && call.Name == "print")
        {
            if (arguments.Length != 1 || call.Arguments.Any(a => a.Name is not null))
                Error("AUV2005", "print requires one positional argument.", call);
            else if (arguments[0] == ValueType.Void) Error("AUV2001", "Cannot print a void result.", call);
            return ValueType.Void;
        }
        Module? target = call.Module is null ? module : module.Imports.GetValueOrDefault(call.Module);
        if (target is null || !target.Functions.TryGetValue(call.Name, out var function))
        { Error("AUV2004", $"Unknown function '{(call.Module is null ? "" : call.Module + ".")}{call.Name}'.", call); return ValueType.Error; }
        Calls[call] = Functions[function];
        if (call.Arguments.Any(a => a.Name is not null)) Error("AUV2005", "Auvil function calls currently use positional arguments.", call);
        if (arguments.Length != function.Parameters.Count) Error("AUV2005", $"'{call.Name}' expects {function.Parameters.Count} arguments, got {arguments.Length}.", call);
        for (var i = 0; i < Math.Min(arguments.Length, function.Parameters.Count); i++) Match(function.Parameters[i].Type, arguments[i], call.Arguments[i]);
        return function.Type;
    }
    private void Match(ValueType expected, ValueType actual, Node node)
    {
        if (expected != actual && expected != ValueType.Error && actual != ValueType.Error)
            Error("AUV2001", $"Expected {expected.ToString().ToLowerInvariant()}, got {actual.ToString().ToLowerInvariant()}.", node);
    }
    private void Error(string code, string message, Node node) => module.Source.Error(code, message, node.Token);
}
