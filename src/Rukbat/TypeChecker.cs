namespace Rukbat;

internal sealed record VariableSymbol(string Name, ValueType Type, string PowerShellName, bool IsGlobal = false);
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
    public HashSet<Call> ArrayAppends { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Call, string> StandardCalls { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Expression, ValueType> Types { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Expression, ValueType> Conversions { get; } = new(ReferenceEqualityComparer.Instance);
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
    }
    // A loop may execute zero times. Both paths of a try/catch must return or throw.
    private static bool AlwaysExits(List<Statement> body) => body.Any(statement =>
        statement is Return or Throw ||
        statement is ExpressionStatement { Value: Call { Receiver: null, Name: "exit", PowerShellType: null } } ||
        statement is Conditional condition &&
            AlwaysExits(condition.Then) && AlwaysExits(condition.Else) ||
        statement is TryCatch guarded &&
            AlwaysExits(guarded.Body) && AlwaysExits(guarded.Handler.Body));

    private void Declare(Node node, ValueType type, Symbols scope, bool global)
    {
        // Callers supply resolved semantic types. Rebinding an inferred struct name in
        // the caller's module would discard its defining module (including inside T[]/T?).
        DeclaredTypes[node] = type;
        if (type == ValueType.Void)
        {
            Error("RUK2001", "Variables and parameters cannot have type void.", node);
        }

        var name = node.Token.Text;
        if (name == "Error" || module.Functions.ContainsKey(name) || module.Imports.ContainsKey(name) || module.Structs.ContainsKey(name))
        {
            Error("RUK2002", $"Name '{name}' is already a function, struct or import.", node);
        }

        var symbol = new VariableSymbol(name, type, $"${(global ? "script:" : "")}__rukbat_v{variableId++}", global);
        if (!scope.Variables.TryAdd(name, symbol))
        {
            Error("RUK2002", $"Duplicate declaration '{name}'.", node);
        }

        Variables[node] = symbol;
    }

    private void CheckStatements(List<Statement> statements, Symbols scope, ValueType? returnType, bool topLevel)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case Import import when !topLevel:
                    Error("RUK3000", "Imports are allowed only at module level.", import);
                    break;
                case StructDeclaration structure:
                    Error("RUK1002", "Structs are allowed only at module level.", structure);
                    break;
                case Function function:
                    Error("RUK1002", "Nested functions are not supported yet.", function);
                    break;
                case Variable variable:
                    var declaredType = variable.IsInferred ? null : BindType(variable.Type, variable);
                    var actual = TypeOf(variable.Value, scope, declaredType);
                    if (variable.IsInferred && actual == ValueType.Null)
                    {
                        Error("RUK2001", "A null literal requires an explicit nullable type.", variable.Value);
                        actual = ValueType.Error;
                    }
                    Declare(variable, declaredType ?? actual, scope, topLevel);
                    Match(Variables[variable].Type, actual, variable.Value);
                    UpdateRefinement(Variables[variable], actual);
                    break;
                case Assignment assignment:
                    var target = scope.Find(assignment.Token.Text);
                    if (target is null)
                    {
                        Error("RUK2004", $"Unknown variable '{assignment.Token.Text}'. Declare it with a type first.", assignment);
                    }
                    else
                    {
                        Variables[assignment] = target;
                        var assignedType = TypeOf(assignment.Value, scope, target.Type);
                        Match(target.Type, assignedType, assignment.Value);
                        UpdateRefinement(target, assignedType);
                    }
                    break;
                case FieldAssignment assignment:
                    var fieldType = TypeOf(assignment.Target, scope);
                    if (!IsVariablePath(assignment.Target))
                    {
                        Error("RUK2010", "Field assignment requires a variable, not a temporary value.", assignment);
                    }

                    if (assignment.Target.Receiver is { } receiver && Types[receiver].Element is not null)
                    {
                        Error("RUK2010", "Array length is read-only.", assignment);
                    }

                    Match(fieldType, TypeOf(assignment.Value, scope, fieldType), assignment.Value, $"field '{assignment.Target.Name}'");
                    break;
                case IndexAssignment assignment:
                    var elementType = TypeOf(assignment.Target, scope);
                    if (!IsVariablePath(assignment.Target))
                    {
                        Error("RUK2010", "Array assignment requires a variable, not a temporary value.", assignment);
                    }

                    Match(elementType, TypeOf(assignment.Value, scope, elementType), assignment.Value);
                    break;
                case For loop:
                    var collectionType = TypeOf(loop.Items, scope);
                    if (collectionType.Element is null && collectionType != ValueType.Error)
                    {
                        Error("RUK2001", "for ... in requires an array.", loop.Items);
                    }

                    var beforeLoop = SnapshotRefinements();
                    foreach (var name in AssignedNames(loop.Body))
                    {
                        var assigned = scope.Find(name);
                        if (assigned is not null)
                        {
                            refinements.Remove(assigned);
                        }
                    }
                    if (ContainsUnqualifiedUserCall(loop.Body))
                    {
                        InvalidateGlobalRefinements();
                    }

                    var loopScope = new Symbols(scope);
                    DeclaredTypes[loop] = collectionType.Element ?? ValueType.Error;
                    Declare(loop, DeclaredTypes[loop], loopScope, false);
                    loopDepth++;
                    CheckStatements(loop.Body, loopScope, returnType, false);
                    loopDepth--;
                    MergeRefinements(beforeLoop, refinements);
                    break;
                case While loop:
                    Match(ValueType.Bool, TypeOf(loop.Condition, scope), loop.Condition);
                    var beforeWhile = SnapshotRefinements();
                    foreach (var name in AssignedNames(loop.Body))
                    {
                        var assigned = scope.Find(name);
                        if (assigned is not null)
                        {
                            refinements.Remove(assigned);
                        }
                    }
                    if (ContainsUnqualifiedUserCall(loop.Body))
                    {
                        InvalidateGlobalRefinements();
                    }

                    var (whileSymbol, nonNullWhileTrue) = NullTestRefinement(loop.Condition);
                    if (whileSymbol is not null && nonNullWhileTrue)
                    {
                        refinements[whileSymbol] = whileSymbol.Type.NonNullable;
                    }
                    loopDepth++;
                    CheckStatements(loop.Body, new Symbols(scope), returnType, false);
                    loopDepth--;
                    MergeRefinements(beforeWhile, refinements);
                    break;
                case LoopControl control:
                    if (loopDepth == 0)
                    {
                        Error("RUK2010", $"{control.Token.Text} is allowed only inside a loop.", control);
                    }

                    break;
                case Return result:
                    var resultType = result.Value is null ? ValueType.Void : TypeOf(result.Value, scope, returnType);
                    if (returnType is null)
                    {
                        Error("RUK2003", "return is allowed only inside a function.", result);
                    }
                    else
                    {
                        Match(returnType, resultType, (Node?)result.Value ?? result);
                    }

                    break;
                case Throw failure:
                    Match(ErrorStructure.BoundType, TypeOf(failure.Value, scope), failure.Value, "throw");
                    break;
                case TryCatch guarded:
                    var beforeTry = SnapshotRefinements();
                    CheckStatements(guarded.Body, new Symbols(scope), returnType, false);
                    var afterTry = SnapshotRefinements();
                    refinements = SnapshotRefinements(beforeTry);
                    foreach (var name in AssignedNames(guarded.Body))
                    {
                        var assigned = scope.Find(name);
                        if (assigned is not null)
                        {
                            refinements.Remove(assigned);
                        }
                    }
                    if (ContainsUnqualifiedUserCall(guarded.Body))
                    {
                        InvalidateGlobalRefinements();
                    }
                    var handlerScope = new Symbols(scope);
                    Declare(guarded.Handler, ErrorStructure.BoundType, handlerScope, false);
                    CheckStatements(guarded.Handler.Body, handlerScope, returnType, false);
                    MergeRefinements(afterTry, refinements);
                    break;
                case Conditional conditional:
                    Match(ValueType.Bool, TypeOf(conditional.Condition, scope), conditional.Condition);
                    CheckConditional(conditional, scope, returnType);
                    break;
                case ExpressionStatement expression:
                    TypeOf(expression.Value, scope);
                    break;
            }
        }
    }

    private ValueType TypeOf(Expression expression, Symbols scope, ValueType? expected = null)
    {
        // Retain each node's checked type so the emitter never needs to infer it again.
        var type = expression switch
        {
            Literal literal => literal.Value switch
            {
                int => ValueType.Int,
                double => ValueType.Float,
                string => ValueType.String,
                bool => ValueType.Bool,
                NullValue => ValueType.Null,
                _ => ValueType.Error
            },
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
        if (symbol is null)
        {
            var message = name.Token.Text == "self"
                ? "self is available only inside a struct method."
                : $"Unknown variable '{name.Token.Text}'.";
            Error("RUK2004", message, name);
            return ValueType.Error;
        }
        Variables[name] = symbol;
        return refinements.GetValueOrDefault(symbol, symbol.Type);
    }

    private ValueType UnaryType(Unary unary, Symbols scope)
    {
        var actual = TypeOf(unary.Value, scope);
        if (unary.Token.Text == "!")
        {
            Match(ValueType.Bool, actual, unary.Value);
            return ValueType.Bool;
        }
        if (actual == ValueType.Int || actual == ValueType.Float)
        {
            return actual;
        }
        Error("RUK2001", $"Unary operator '{unary.Token.Text}' requires int or float, got {actual}.", unary);
        return ValueType.Error;
    }

    private ValueType BinaryType(Binary binary, Symbols scope)
    {
        var left = TypeOf(binary.Left, scope);
        var right = TypeOf(binary.Right, scope);
        if (left == ValueType.Error || right == ValueType.Error)
        {
            return ValueType.Error;
        }

        var op = binary.Token.Text;
        if (op is "==" or "!=" && IsNullableNullComparison(binary))
        {
            return ValueType.Bool;
        }
        if (left == right && !left.IsNullable && !left.IsComposite && left != ValueType.Void && op is "==" or "!=")
        {
            return ValueType.Bool;
        }

        if (left == ValueType.Bool && right == ValueType.Bool && op is "&&" or "||")
        {
            return ValueType.Bool;
        }

        if (left == ValueType.Int && right == ValueType.Int)
        {
            return op switch
            {
                "<" or "<=" or ">" or ">=" => ValueType.Bool,
                "+" or "-" or "*" or "/" or "%" => ValueType.Int,
                _ => Invalid()
            };
        }

        if (IsNumber(left) && IsNumber(right))
        {
            return op switch
            {
                "==" or "!=" or "<" or "<=" or ">" or ">=" => ValueType.Bool,
                "+" or "-" or "*" or "/" => ValueType.Float,
                _ => Invalid()
            };
        }

        if (left == ValueType.String && right == ValueType.String && op == "+")
        {
            return ValueType.String;
        }

        return Invalid();

        ValueType Invalid()
        {
            Error("RUK2001", $"Operator '{op}' cannot combine {left} and {right}.", binary);
            return ValueType.Error;
        }
    }

    private ValueType CallType(Call call, Symbols scope)
    {
        if (call.Receiver is Name standardAlias &&
            module.Imports.TryGetValue(standardAlias.Token.Text, out var standardModule) &&
            standardModule.StandardName is { } standardName)
        {
            return StandardCallType(call, scope, standardModule, standardName);
        }
        if (call.PowerShellType is null && (call.Receiver is not null || call.Name is not ("print" or "eprint" or "args" or "exit")))
        {
            return ResolveCall(call, scope);
        }

        var arguments = call.Arguments.Select(a => TypeOf(a.Value, scope)).ToArray();
        if (call.Receiver is null && call.Name == "exit" && call.PowerShellType is null)
        {
            if (arguments.Length != 1 || call.Arguments.Any(a => a.Name is not null))
                Error("RUK2005", "exit requires one positional argument.", call);
            else
                Match(ValueType.Int, arguments[0], call.Arguments[0].Value);
            return ValueType.Void;
        }
        if (call.Receiver is null && call.Name == "args")
        {
            if (arguments.Length != 0)
            {
                Error("RUK2005", "args requires no arguments.", call);
            }
            return ValueType.ArrayOf(ValueType.String);
        }
        if (call.PowerShellType is { } powerShellType)
        {
            powerShellType = BindType(powerShellType, call);

            if (arguments.Length == 0 || call.Arguments[0].Name is not null)
            {
                Error("RUK2005", "powershell<T> requires a command name as its first positional argument.", call);
            }
            else
            {
                Match(ValueType.String, arguments[0], call.Arguments[0].Value);
            }

            var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sawNamed = false;
            for (var i = 0; i < call.Arguments.Count; i++)
            {
                var argument = call.Arguments[i];
                if (ContainsStruct(arguments[i]))
                {
                    Error("RUK2010", "Structs cannot be passed directly to PowerShell. Pass primitive fields.", argument);
                }

                if (arguments[i] == ValueType.Void)
                {
                    Error("RUK2001", "A void result cannot be passed as an argument.", argument);
                }

                if (argument.Name is { } name)
                {
                    sawNamed = true;
                    if (!named.Add(name))
                    {
                        Error("RUK2005", $"Duplicate PowerShell parameter '{name}'.", argument);
                    }
                }
                else if (sawNamed)
                {
                    Error("RUK2005", "Positional arguments must precede named arguments.", argument);
                }
            }
            return powerShellType;
        }
        if (call.Receiver is null && call.Name is "print" or "eprint")
        {
            if (arguments.Length != 1 || call.Arguments.Any(a => a.Name is not null))
            {
                Error("RUK2005", $"{call.Name} requires one positional argument.", call);
            }
            else if (arguments[0].IsComposite)
            {
                Error("RUK2010", "Print individual struct fields or array elements rather than the composite value.", call);
            }
            else if (arguments[0].IsNullable)
            {
                Error("RUK2001", "A nullable value must be checked against null before it can be printed.", call.Arguments[0].Value);
            }
            else if (arguments[0] == ValueType.Void)
            {
                Error("RUK2001", "Cannot print a void result.", call);
            }

            return ValueType.Void;
        }
        return ValueType.Error;
    }

    private ValueType StandardCallType(Call call, Symbols scope, Module standardModule, string moduleName)
    {
        if (moduleName == "json" && call.Name is "encode" or "decode")
        {
            return JsonCallType(call, scope);
        }

        ValueType[]? parameters = (moduleName, call.Name) switch
        {
            ("files", "list") => [ValueType.String, ValueType.String],
            ("files", "read") or ("files", "exists") => [ValueType.String],
            ("files", "write") => [ValueType.String, ValueType.String],
            ("path", "script_dir") => [],
            ("path", "join") => [ValueType.ArrayOf(ValueType.String)],
            ("path", "filename") or ("path", "extension") => [ValueType.String],
            ("env", "get") or ("env", "remove") => [ValueType.String],
            ("env", "set") => [ValueType.String, ValueType.String],
            ("process", "run") => [ValueType.String, ValueType.ArrayOf(ValueType.String)],
            ("text", "from_int") => [ValueType.Int],
            ("text", "from_float") => [ValueType.Float],
            ("text", "from_bool") => [ValueType.Bool],
            ("text", "parse_int" or "parse_float" or "parse_bool") => [ValueType.String],
            _ => null
        };
        if (parameters is null)
        {
            Error("RUK2004", $"Unknown standard-library function '{moduleName}.{call.Name}'.", call);
            return ValueType.Error;
        }
        if (call.TypeArgument is not null)
        {
            Error("RUK2005", $"{moduleName}.{call.Name} does not accept a type argument.", call);
        }
        CheckStandardArguments(call, scope, parameters);
        var returnType = (moduleName, call.Name) switch
        {
            ("files", "list") => ValueType.ArrayOf(ValueType.String),
            ("files", "read") => ValueType.String,
            ("files", "exists") => ValueType.Bool,
            ("files", "write") => ValueType.Void,
            ("path", _) => ValueType.String,
            ("env", "get") => ValueType.NullableOf(ValueType.String),
            ("env", _) => ValueType.Void,
            ("process", "run") => standardModule.Structs["Result"].BoundType,
            ("text", "parse_int") => ValueType.Int,
            ("text", "parse_float") => ValueType.Float,
            ("text", "parse_bool") => ValueType.Bool,
            ("text", _) => ValueType.String,
            _ => ValueType.Error
        };
        StandardCalls[call] = $"{moduleName}.{call.Name}";
        return returnType;
    }

    private void CheckStandardArguments(Call call, Symbols scope, IReadOnlyList<ValueType> parameters)
    {
        if (call.Arguments.Count != parameters.Count || call.Arguments.Any(argument => argument.Name is not null))
        {
            Error("RUK2005", $"{call.Name} expects {parameters.Count} positional arguments, got {call.Arguments.Count}.", call);
        }
        for (var index = 0; index < call.Arguments.Count; index++)
        {
            var expected = index < parameters.Count ? parameters[index] : null;
            var actual = TypeOf(call.Arguments[index].Value, scope, expected);
            if (expected is not null)
            {
                Match(expected, actual, call.Arguments[index].Value, $"argument {index + 1}");
            }
        }
    }

    private ValueType JsonCallType(Call call, Symbols scope)
    {
        if (call.Arguments.Count != 1 || call.Arguments.Any(argument => argument.Name is not null))
        {
            Error("RUK2005", $"json.{call.Name} requires one positional argument.", call);
        }
        var argumentType = call.Arguments.Count == 0
            ? ValueType.Error
            : TypeOf(call.Arguments[0].Value, scope);
        if (call.Name == "decode")
        {
            if (call.TypeArgument is null)
            {
                Error("RUK2005", "json.decode requires an explicit result type: json.decode<T>(text).", call);
                return ValueType.Error;
            }
            Match(ValueType.String, argumentType, call.Arguments.Count == 0 ? call : call.Arguments[0].Value);
            var resultType = BindType(call.TypeArgument, call);
            if (!SupportsJson(resultType))
            {
                Error("RUK2010", $"Type {resultType} cannot be decoded from JSON.", call);
            }
            StandardCalls[call] = "json.decode";
            return resultType;
        }
        if (call.TypeArgument is not null)
        {
            Error("RUK2005", "json.encode infers its input type and does not accept a type argument.", call);
        }
        if (!SupportsJson(argumentType))
        {
            Error("RUK2010", $"Type {argumentType} cannot be encoded as JSON.", call);
        }
        StandardCalls[call] = "json.encode";
        return ValueType.String;
    }

    private bool SupportsJson(ValueType type)
    {
        if (type.IsNullable)
        {
            return SupportsJson(type.NonNullable);
        }
        if (type.Element is { } element)
        {
            return SupportsJson(element);
        }
        return type is { Struct: not null } && type.Struct != ErrorStructure ||
            type == ValueType.Bool || type == ValueType.Int || type == ValueType.Float || type == ValueType.String;
    }

    private void CheckConditional(Conditional conditional, Symbols scope, ValueType? returnType)
    {
        var baseline = SnapshotRefinements();
        var (symbol, nonNullWhenTrue) = NullTestRefinement(conditional.Condition);

        refinements = SnapshotRefinements(baseline);
        if (symbol is not null && nonNullWhenTrue)
        {
            refinements[symbol] = symbol.Type.NonNullable;
        }
        CheckStatements(conditional.Then, new Symbols(scope), returnType, false);
        var afterThen = SnapshotRefinements();

        refinements = SnapshotRefinements(baseline);
        if (symbol is not null && !nonNullWhenTrue)
        {
            refinements[symbol] = symbol.Type.NonNullable;
        }
        CheckStatements(conditional.Else, new Symbols(scope), returnType, false);
        var afterElse = SnapshotRefinements();

        var thenExits = AlwaysExits(conditional.Then);
        var elseExits = conditional.Else.Count != 0 && AlwaysExits(conditional.Else);
        if (thenExits && !elseExits)
        {
            refinements = afterElse;
        }
        else if (elseExits && !thenExits)
        {
            refinements = afterThen;
        }
        else
        {
            MergeRefinements(afterThen, afterElse);
        }
    }

    private static IEnumerable<string> AssignedNames(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            if (statement is Assignment assignment)
            {
                yield return assignment.Token.Text;
            }
            var nested = statement switch
            {
                Conditional conditional => conditional.Then.Concat(conditional.Else),
                For loop => loop.Body,
                While loop => loop.Body,
                TryCatch guarded => guarded.Body.Concat(guarded.Handler.Body),
                _ => []
            };
            foreach (var name in AssignedNames(nested))
            {
                yield return name;
            }
        }
    }

    private static bool ContainsUnqualifiedUserCall(IEnumerable<Statement> statements) => statements.Any(statement =>
        StatementExpressions(statement).Any(ContainsUnqualifiedUserCall) || statement switch
        {
            Conditional conditional => ContainsUnqualifiedUserCall(conditional.Then) || ContainsUnqualifiedUserCall(conditional.Else),
            For loop => ContainsUnqualifiedUserCall(loop.Body),
            While loop => ContainsUnqualifiedUserCall(loop.Body),
            TryCatch guarded => ContainsUnqualifiedUserCall(guarded.Body) || ContainsUnqualifiedUserCall(guarded.Handler.Body),
            _ => false
        });

    private static IEnumerable<Expression> StatementExpressions(Statement statement) => statement switch
    {
        Variable variable => [variable.Value],
        Assignment assignment => [assignment.Value],
        FieldAssignment assignment => [assignment.Target, assignment.Value],
        IndexAssignment assignment => [assignment.Target, assignment.Value],
        Return { Value: { } value } => [value],
        Throw failure => [failure.Value],
        For loop => [loop.Items],
        While loop => [loop.Condition],
        Conditional conditional => [conditional.Condition],
        ExpressionStatement expression => [expression.Value],
        _ => []
    };

    private static bool ContainsUnqualifiedUserCall(Expression expression)
    {
        if (expression is Call { Receiver: null } directCall && directCall.Name is not ("print" or "eprint" or "args" or "powershell" or "Error"))
        {
            return true;
        }
        return expression switch
        {
            Unary unary => ContainsUnqualifiedUserCall(unary.Value),
            Binary binary => ContainsUnqualifiedUserCall(binary.Left) || ContainsUnqualifiedUserCall(binary.Right),
            ArrayLiteral array => array.Items.Any(ContainsUnqualifiedUserCall),
            Index index => ContainsUnqualifiedUserCall(index.Receiver) || ContainsUnqualifiedUserCall(index.Offset),
            Member member => ContainsUnqualifiedUserCall(member.Receiver),
            Call call => (call.Receiver is not null && ContainsUnqualifiedUserCall(call.Receiver)) ||
                call.Arguments.Any(argument => ContainsUnqualifiedUserCall(argument.Value)),
            _ => false
        };
    }

    private (VariableSymbol? Symbol, bool NonNullWhenTrue) NullTestRefinement(Expression condition)
    {
        if (condition is not Binary { Token.Text: "==" or "!=" } binary)
        {
            return (null, false);
        }

        var name = IsNullLiteral(binary.Left) ? binary.Right as Name :
            IsNullLiteral(binary.Right) ? binary.Left as Name : null;
        if (name is null || !Variables.TryGetValue(name, out var symbol) || !symbol.Type.IsNullable)
        {
            return (null, false);
        }
        return (symbol, binary.Token.Text == "!=");
    }

    private bool IsNullableNullComparison(Binary binary)
    {
        var other = IsNullLiteral(binary.Left) ? binary.Right :
            IsNullLiteral(binary.Right) ? binary.Left : null;
        if (other is null)
        {
            return false;
        }
        return Types[other].IsNullable || other is Name name &&
            Variables.TryGetValue(name, out var symbol) && symbol.Type.IsNullable;
    }

    private static bool IsNullLiteral(Expression expression) =>
        expression is Literal { Value: NullValue };

    private static bool IsNumber(ValueType type) => type == ValueType.Int || type == ValueType.Float;

    private void UpdateRefinement(VariableSymbol symbol, ValueType assigned)
    {
        refinements.Remove(symbol);
        if (symbol.Type.IsNullable && assigned != ValueType.Null && !assigned.IsNullable &&
            CanConvert(symbol.Type.NonNullable, assigned))
        {
            refinements[symbol] = symbol.Type.NonNullable;
        }
    }

    private Dictionary<VariableSymbol, ValueType> SnapshotRefinements() =>
        SnapshotRefinements(refinements);

    private static Dictionary<VariableSymbol, ValueType> SnapshotRefinements(
        Dictionary<VariableSymbol, ValueType> source) => new(source);

    private void MergeRefinements(params Dictionary<VariableSymbol, ValueType>[] states)
    {
        refinements = states.Length == 0 ? [] : states[0]
            .Where(pair => states.Skip(1).All(state => state.TryGetValue(pair.Key, out var type) && type == pair.Value))
            .ToDictionary();
    }

    private void InvalidateGlobalRefinements()
    {
        foreach (var symbol in refinements.Keys.Where(symbol => symbol.IsGlobal).ToArray())
        {
            refinements.Remove(symbol);
        }
    }

    private static bool CanConvert(ValueType expected, ValueType actual)
    {
        if (expected == actual || expected == ValueType.Error || actual == ValueType.Error)
        {
            return true;
        }
        if (expected.IsNullable)
        {
            if (actual == ValueType.Null)
            {
                return true;
            }
            return actual.IsNullable
                ? CanConvert(expected.NonNullable, actual.NonNullable)
                : CanConvert(expected.NonNullable, actual);
        }
        return expected == ValueType.Float && actual == ValueType.Int;
    }

    private void Match(ValueType expected, ValueType actual, Node node, string? context = null)
    {
        if (CanConvert(expected, actual))
        {
            if (node is Expression expression && expected != actual &&
                expected != ValueType.Error && actual != ValueType.Error)
            {
                Conversions[expression] = expected;
            }
            return;
        }

        var expectedName = expected.Name;
        var actualName = actual.Name;
        if (expectedName == actualName && expected.IsComposite && actual.IsComposite)
        {
            expectedName = QualifiedTypeName(expected);
            actualName = QualifiedTypeName(actual);
        }
        Error("RUK2001", $"Expected {expectedName}{(context is null ? "" : " for " + context)}, got {actualName}.", node);
    }

    private static string QualifiedTypeName(ValueType type)
    {
        if (type.NullableElement is { } nullable)
        {
            return QualifiedTypeName(nullable) + "?";
        }
        if (type.Element is { } element)
        {
            return QualifiedTypeName(element) + "[]";
        }

        if (type.Struct is { } structure)
        {
            return Path.GetFileNameWithoutExtension(structure.Module.Source.Path) + "." + type.Name;
        }

        return type.Name;
    }
    private void Error(string code, string message, Node node) => module.Source.Error(code, message, node.Token);
}
