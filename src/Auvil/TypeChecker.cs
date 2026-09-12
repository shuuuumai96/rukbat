namespace Auvil;

internal sealed record VariableSymbol(string Name, ValueType Type, string PowerShellName);
internal sealed record FunctionSymbol(Module Module, Function Function, string PowerShellName, StructSymbol? Owner = null)
{
    public ValueType ReturnType { get; set; } = ValueType.Error;
}
internal sealed class Symbols(Symbols? parent = null)
{
    public Dictionary<string, VariableSymbol> Variables { get; } = new(StringComparer.Ordinal);
    public VariableSymbol? Find(string name) => Variables.GetValueOrDefault(name) ?? parent?.Find(name);
}

internal sealed partial class TypeChecker
{
    public Dictionary<Node, VariableSymbol> Variables { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Function, FunctionSymbol> Functions { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Call, FunctionSymbol> Calls { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Expression, ValueType> Types { get; } = new(ReferenceEqualityComparer.Instance);
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
            CheckStatements(current.Statements.Where(s => s is not Function and not StructDeclaration).ToList(), globals, null, true);
            foreach (var symbol in Functions.Values.Where(f => f.Module == current))
            {
                var function = symbol.Function;
                if (symbol.Owner is null && current.Imports.ContainsKey(function.Name)) Error("AUV2002", $"Name '{function.Name}' is already an import.", function);
                var locals = new Symbols(globals);
                if (symbol.Owner is { } owner)
                    locals.Variables.Add("self", new("self", owner.BoundType, "$__auv_self"));
                foreach (var parameter in function.Parameters) Declare(parameter, parameter.Type, locals, false);
                CheckStatements(function.Body, locals, symbol.ReturnType, false);
                if (symbol.ReturnType != ValueType.Void && !AlwaysReturns(function.Body))
                    Error("AUV2003", $"Function '{function.Name}' must return a value on every path.", function);
            }
        }
    }
    private static bool AlwaysReturns(List<Statement> body) => body.Any(s => s is Return ||
        s is Conditional condition && AlwaysReturns(condition.Then) && AlwaysReturns(condition.Else));

    private void Declare(Node node, ValueType type, Symbols scope, bool global)
    {
        type = DeclaredTypes.TryGetValue(node, out var declared) ? declared : BindType(type, node);
        if (type == ValueType.Void) Error("AUV2001", "Variables and parameters cannot have type void.", node);
        var name = node.Token.Text;
        if (module.Functions.ContainsKey(name) || module.Imports.ContainsKey(name) || module.Structs.ContainsKey(name))
            Error("AUV2002", $"Name '{name}' is already a function, struct or import.", node);
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
                case StructDeclaration structure: Error("AUV1002", "Structs are allowed only at module level.", structure); break;
                case Function function: Error("AUV1002", "Nested functions are not supported yet.", function); break;
                case Variable variable:
                    var declaredType = BindType(variable.Type, variable);
                    var actual = TypeOf(variable.Value, scope, declaredType);
                    Declare(variable, variable.Type, scope, topLevel);
                    Match(Variables[variable].Type, actual, variable.Value);
                    break;
                case Assignment assignment:
                    var target = scope.Find(assignment.Token.Text);
                    if (target is null) Error("AUV2004", $"Unknown variable '{assignment.Token.Text}'. Declare it with a type first.", assignment);
                    else { Variables[assignment] = target; Match(target.Type, TypeOf(assignment.Value, scope, target.Type), assignment.Value); }
                    break;
                case FieldAssignment assignment:
                    var fieldType = TypeOf(assignment.Target, scope);
                    if (!IsVariablePath(assignment.Target)) Error("AUV2010", "Field assignment requires a variable, not a temporary value.", assignment);
                    if (assignment.Target.Receiver is { } receiver && Types[receiver].Element is not null)
                        Error("AUV2010", "Array length is read-only.", assignment);
                    Match(fieldType, TypeOf(assignment.Value, scope, fieldType), assignment.Value, $"field '{assignment.Target.Name}'");
                    break;
                case IndexAssignment assignment:
                    var elementType = TypeOf(assignment.Target, scope);
                    if (!IsVariablePath(assignment.Target)) Error("AUV2010", "Array assignment requires a variable, not a temporary value.", assignment);
                    Match(elementType, TypeOf(assignment.Value, scope, elementType), assignment.Value);
                    break;
                case For loop:
                    var collectionType = TypeOf(loop.Items, scope);
                    if (collectionType.Element is null && collectionType != ValueType.Error)
                        Error("AUV2001", "for ... in requires an array.", loop.Items);
                    var loopScope = new Symbols(scope);
                    DeclaredTypes[loop] = collectionType.Element ?? ValueType.Error;
                    Declare(loop, DeclaredTypes[loop], loopScope, false);
                    loopDepth++;
                    CheckStatements(loop.Body, loopScope, returnType, false);
                    loopDepth--;
                    break;
                case LoopControl control:
                    if (loopDepth == 0) Error("AUV2010", $"{control.Token.Text} is allowed only inside a loop.", control);
                    break;
                case Return result:
                    var resultType = result.Value is null ? ValueType.Void : TypeOf(result.Value, scope, returnType);
                    if (returnType is null) Error("AUV2003", "return is allowed only inside a function.", result);
                    else Match(returnType, resultType, result);
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
    private ValueType TypeOf(Expression expression, Symbols scope, ValueType? expected = null)
    {
        var type = expression switch
        {
            Literal literal => literal.Value switch { int => ValueType.Int, string => ValueType.String, bool => ValueType.Bool, _ => ValueType.Error },
            ArrayLiteral array => ArrayType(array, scope, expected),
            Index index => IndexType(index, scope),
            Name name => Resolve(name, scope),
            Member member => MemberType(member, scope),
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
        if (symbol is null) { Error("AUV2004", name.Token.Text == "self" ? "self is available only inside a struct method." : $"Unknown variable '{name.Token.Text}'.", name); return ValueType.Error; }
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
        if (left == right && !left.IsComposite && left != ValueType.Void && op is "==" or "!=") return ValueType.Bool;
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
        if (call.PowerShellType is null && (call.Receiver is not null || call.Name != "print")) return ResolveCall(call, scope);
        var arguments = call.Arguments.Select(a => TypeOf(a.Value, scope)).ToArray();
        if (call.PowerShellType is { } powerShellType)
        {
            powerShellType = BindType(powerShellType, call);

            if (arguments.Length == 0 || call.Arguments[0].Name is not null)
                Error("AUV2005", "powershell<T> requires a command name as its first positional argument.", call);
            else Match(ValueType.String, arguments[0], call.Arguments[0]);
            var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sawNamed = false;
            for (var i = 0; i < call.Arguments.Count; i++)
            {
                var argument = call.Arguments[i];
                if (ContainsStruct(arguments[i])) Error("AUV2010", "Structs cannot be passed directly to PowerShell. Pass primitive fields.", argument);
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
        if (call.Receiver is null && call.Name == "print")
        {
            if (arguments.Length != 1 || call.Arguments.Any(a => a.Name is not null))
                Error("AUV2005", "print requires one positional argument.", call);
            else if (arguments[0].IsComposite) Error("AUV2010", "Print individual struct fields or array elements rather than the composite value.", call);
            else if (arguments[0] == ValueType.Void) Error("AUV2001", "Cannot print a void result.", call);
            return ValueType.Void;
        }
        return ValueType.Error;
    }
    private void Match(ValueType expected, ValueType actual, Node node, string? context = null)
    {
        if (expected == actual || expected == ValueType.Error || actual == ValueType.Error) return;
        var expectedName = expected.Name;
        var actualName = actual.Name;
        if (expectedName == actualName && expected.IsComposite && actual.IsComposite)
        {
            expectedName = QualifiedTypeName(expected);
            actualName = QualifiedTypeName(actual);
        }
        Error("AUV2001", $"Expected {expectedName}{(context is null ? "" : " for " + context)}, got {actualName}.", node);
    }
    private static string QualifiedTypeName(ValueType type) => type.Element is { } element
        ? QualifiedTypeName(element) + "[]"
        : type.Struct is { } structure
            ? Path.GetFileNameWithoutExtension(structure.Module.Source.Path) + "." + type.Name
            : type.Name;
    private void Error(string code, string message, Node node) => module.Source.Error(code, message, node.Token);
}
