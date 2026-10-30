namespace Rukbat;

internal sealed class StructSymbol(Module module, StructDeclaration declaration)
{
    public Module Module { get; } = module;
    public StructDeclaration Declaration { get; } = declaration;
    public ValueType Type { get; } = new(module.StandardName is "json" or "csv" or "test" or "cli" or "log" or "re"
        ? module.StandardName + "." + declaration.Token.Text : declaration.Token.Text);
    public bool IsCsvRow => Module.StandardName == "csv" && Declaration.Token.Text == "Row";
    public bool IsJsonValue => Module.StandardName == "json" && Declaration.Token.Text == "Value";
    public bool IsCliResult => Module.StandardName == "cli" && Declaration.Token.Text == "Result";
    public Dictionary<string, FieldSymbol> Fields { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, FunctionSymbol> Methods { get; } = new(StringComparer.Ordinal);
    public ValueType BoundType => Type with { Struct = this };
}
internal sealed record FieldSymbol(Field Field, ValueType Type, string Key);

internal sealed partial class TypeChecker
{
    public StructSymbol ErrorStructure { get; } = CreateErrorStructure();

    private static StructSymbol CreateErrorStructure()
    {
        var declaration = new StructDeclaration(new("Error", 0), []);
        var owner = new Module(new Source("", "<builtin>"), "builtin", []);
        var structure = new StructSymbol(owner, declaration);
        var field = new Field(new("message", 0), ValueType.String);
        structure.Fields.Add("message", new(field, ValueType.String, "message"));
        return structure;
    }

    public Dictionary<Node, ValueType> DeclaredTypes { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Member, FieldSymbol> Fields { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Call, StructSymbol> Constructions { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Argument, FieldSymbol> ConstructionFields { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<ImplDeclaration, StructSymbol> Implementations { get; } = new(ReferenceEqualityComparer.Instance);

    private void RegisterDefinitions(List<Module> modules)
    {
        // Register names across all modules before binding field and function types.
        // This allows a declaration to refer to a type defined later in the same module.
        foreach (var current in modules)
        {
            module = current;
            if (current.Imports.ContainsKey("Error"))
            {
                Error("RUK2002", "Import name 'Error' conflicts with the built-in type.",
                    current.Statements.OfType<Import>().First(import => import.Name == "Error"));
            }
            foreach (var declaration in current.Statements.OfType<StructDeclaration>())
            {
                var name = declaration.Token.Text;
                if (!current.Structs.TryAdd(name, new(current, declaration)))
                {
                    Error("RUK2002", $"Duplicate struct '{name}'.", declaration);
                }

                if (IsPrimitive(name) || name is "Error" or "dict" || current.Imports.ContainsKey(name))
                {
                    Error("RUK2002", $"Struct name '{name}' conflicts with an existing type or import.", declaration);
                }
            }
            foreach (var function in current.Statements.OfType<Function>())
            {
                if (!current.Functions.TryAdd(function.Name, function))
                {
                    Error("RUK2002", $"Duplicate function '{function.Name}'.", function);
                }

                if (current.Structs.ContainsKey(function.Name))
                {
                    Error("RUK2002", $"Function '{function.Name}' conflicts with a struct.", function);
                }
                if (function.Name == "Error")
                {
                    Error("RUK2002", "Function 'Error' conflicts with the built-in type.", function);
                }

                Functions[function] = new(current, function, $"__rukbat_f{Functions.Count}");
            }
        }
        foreach (var current in modules)
        {
            module = current;
            foreach (var structure in current.Structs.Values)
            {
                foreach (var field in structure.Declaration.Fields)
                {
                    var type = BindType(field.Type, field);
                    if (type == ValueType.Void)
                    {
                        Error("RUK2001", $"Field '{field.Token.Text}' cannot have type void.", field);
                    }

                    var symbol = new FieldSymbol(field, type, $"__rukbat_field{structure.Fields.Count}");
                    if (!structure.Fields.TryAdd(field.Token.Text, symbol))
                    {
                        Error("RUK2002", $"Duplicate field '{field.Token.Text}'.", field);
                    }
                }
            }
            var implemented = new HashSet<StructSymbol>();
            foreach (var implementation in current.Statements.OfType<ImplDeclaration>())
            {
                if (!current.Structs.TryGetValue(implementation.Token.Text, out var structure))
                {
                    Error("RUK2004", $"Impl target '{implementation.Token.Text}' must be a struct declared in the same module.", implementation);
                    continue;
                }
                if (!implemented.Add(structure))
                    Error("RUK2002", $"Duplicate impl for struct '{implementation.Token.Text}'. Define its methods in one impl block.", implementation);
                Implementations[implementation] = structure;
                foreach (var method in implementation.Methods)
                {
                    var symbol = new FunctionSymbol(current, method, $"__rukbat_f{Functions.Count}", structure);
                    Functions[method] = symbol;
                    if (!structure.Methods.TryAdd(method.Name, symbol))
                    {
                        Error("RUK2002", $"Duplicate method '{method.Name}'.", method);
                    }

                    if (structure.Fields.ContainsKey(method.Name))
                    {
                        Error("RUK2002", $"Method '{method.Name}' conflicts with a field.", method);
                    }
                }
            }
        }
        foreach (var function in Functions.Values)
        {
            module = function.Module;
            function.ReturnType = BindType(function.Function.Type, function.Function);
            foreach (var parameter in function.Function.Parameters)
            {
                BindType(parameter.Type, parameter);
            }
        }
        // Direct recursion cannot form finite values. Array-mediated recursion is also
        // deferred until recursive type schemas and runtime conversion have explicit support.
        var visited = new HashSet<StructSymbol>();
        var active = new HashSet<StructSymbol>();
        foreach (var structure in modules.SelectMany(m => m.Structs.Values))
        {
            CheckFinite(structure, visited, active);
        }
    }

    private static bool IsPrimitive(string name) => name is "int" or "float" or "string" or "bool" or "void";
    public Dictionary<ValueType, ValueType> BoundTypes { get; } = new(ReferenceEqualityComparer.Instance);
    private ValueType BindType(ValueType type, Node node) => BoundTypes[type] = BindTypeCore(type, node);
    private ValueType BindTypeCore(ValueType type, Node node)
    {
        if (type.DictionaryKey is { } key)
        {
            var boundKey = BindType(key, node);
            var boundValue = BindType(type.DictionaryValue!, node);
            if (boundKey != ValueType.String && boundKey != ValueType.Int && boundKey != ValueType.Error)
                Error("RUK2001", "Dictionary keys must be non-null string or int values.", node);
            if (boundValue == ValueType.Void)
                Error("RUK2001", "Dictionary values cannot have type void.", node);
            return DeclaredTypes[node] = ValueType.DictionaryOf(boundKey, boundValue);
        }
        if (type.NullableElement is { } nullableElement)
        {
            if (nullableElement.NullableElement is not null)
            {
                Error("RUK2001", "Nested nullable types such as T?? are not supported.", node);
                return DeclaredTypes[node] = ValueType.Error;
            }

            var bound = BindType(nullableElement, node);
            if (bound == ValueType.Void)
            {
                Error("RUK2001", "void cannot be nullable.", node);
                return DeclaredTypes[node] = ValueType.Error;
            }

            return DeclaredTypes[node] = ValueType.NullableOf(bound);
        }
        if (type.Element is { } element)
        {
            var bound = BindType(element, node);
            if (bound == ValueType.Void)
            {
                Error("RUK2001", "Array elements cannot have type void.", node);
            }

            return DeclaredTypes[node] = ValueType.ArrayOf(bound);
        }
        if (IsPrimitive(type.Name))
        {
            return DeclaredTypes[node] = type;
        }
        if (type.Name == "Error")
        {
            return DeclaredTypes[node] = ErrorStructure.BoundType;
        }

        var parts = type.Name.Split('.');
        var owner = parts.Length == 1 ? module : module.Imports.GetValueOrDefault(parts[0]);
        if (owner?.StandardName == "fs" && parts[^1] == "Lines")
            return DeclaredTypes[node] = ValueType.FileLines;
        if (owner?.StandardName == "re" && parts[^1] == "Pattern")
            return DeclaredTypes[node] = ValueType.RegexPattern;
        if (owner?.StandardName == "http" && HttpValues.OpaqueType(parts[^1]) is { } httpType)
            return DeclaredTypes[node] = httpType;
        if (owner?.StandardName == "datetime" && DateTimeValues.All.FirstOrDefault(value => value.Name == "datetime." + parts[^1]) is { } dateTimeType)
            return DeclaredTypes[node] = dateTimeType;
        if (owner?.StandardName == "hash" && parts[^1] == "Algorithm")
            return DeclaredTypes[node] = HashValues.Algorithm;
        if (owner?.StandardName == "log" && parts[^1] == "Level")
            return DeclaredTypes[node] = LogValues.Level;
        if (owner?.StandardName == "encoding" && parts[^1] == "Encoding")
        {
            return DeclaredTypes[node] = ValueType.Encoding;
        }
        if (owner is not null && owner.Structs.TryGetValue(parts[^1], out var structure))
        {
            return DeclaredTypes[node] = structure.BoundType;
        }

        Error("RUK2004", $"Unknown struct/type '{type.Name}'.", node);
        return DeclaredTypes[node] = ValueType.Error;
    }

    private static void CheckFinite(StructSymbol structure, HashSet<StructSymbol> visited, HashSet<StructSymbol> active)
    {
        if (visited.Contains(structure))
        {
            return;
        }

        active.Add(structure);
        foreach (var field in structure.Fields.Values)
        {
            var fieldType = field.Type;
            while (fieldType.NullableElement is not null || fieldType.Element is not null || fieldType.DictionaryValue is not null)
            {
                fieldType = fieldType.NullableElement ?? fieldType.Element ?? fieldType.DictionaryValue!;
            }

            if (fieldType.Struct is not { } nested)
            {
                continue;
            }

            if (active.Contains(nested))
            {
                structure.Module.Source.Error("RUK2010", $"Recursive value field '{field.Field.Token.Text}' is not supported.", field.Field.Token);
            }
            else
            {
                CheckFinite(nested, visited, active);
            }
        }
        active.Remove(structure);
        visited.Add(structure);
    }

    private ValueType MemberType(Member member, Symbols scope)
    {
        if (member.Receiver is Name logName && module.Imports.TryGetValue(logName.Token.Text, out var logModule) && logModule.StandardName == "log")
        {
            var value = LogValues.Levels.FirstOrDefault(value => value.Name == member.Name);
            if (value is null)
            {
                Error("RUK2004", $"Unknown logging level '{member.Name}'.", member);
                return ValueType.Error;
            }
            LogConstants[member] = value;
            return LogValues.Level;
        }
        if (member.Receiver is Name httpName && module.Imports.TryGetValue(httpName.Token.Text, out var httpModule) && httpModule.StandardName == "http")
        {
            var value = HttpValues.Constants.FirstOrDefault(value => value.Name == member.Name);
            if (value is null)
            {
                Error("RUK2004", $"Unknown HTTP constant '{member.Name}'.", member);
                return ValueType.Error;
            }
            HttpConstants[member] = value;
            return value.Type;
        }
        if (member.Receiver is Name name && module.Imports.TryGetValue(name.Token.Text, out var imported) &&
            imported.StandardName == "encoding")
        {
            var value = EncodingValues.All.FirstOrDefault(value => value.Name == member.Name);
            if (value is null)
            {
                Error("RUK2004", $"Unknown encoding value '{member.Name}'.", member);
                return ValueType.Error;
            }
            EncodingConstants[member] = value;
            return ValueType.Encoding;
        }
        if (member.Receiver is Name hashName && module.Imports.TryGetValue(hashName.Token.Text, out var hashModule) &&
            hashModule.StandardName == "hash")
        {
            if (!HashValues.All.Contains(member.Name)) {
                Error("RUK2004", $"Unknown hash algorithm '{member.Name}'.", member);
                return ValueType.Error;
            }
            HashConstants[member] = member.Name;
            return HashValues.Algorithm;
        }
        if (member.Receiver is Name dateTimeName && module.Imports.TryGetValue(dateTimeName.Token.Text, out var dateTimeModule) &&
            dateTimeModule.StandardName == "datetime")
        {
            var value = DateTimeValues.Zones.FirstOrDefault(value => value.Name == member.Name);
            if (value is null)
            {
                Error("RUK2004", $"Unknown datetime constant '{member.Name}'.", member);
                return ValueType.Error;
            }
            ZoneConstants[member] = value;
            return DateTimeValues.Zone;
        }
        var type = TypeOf(member.Receiver, scope);
        if (type == ValueType.Error)
        {
            return type;
        }

        if ((type.Element is not null || type.DictionaryKey is not null) && member.Name == "length")
        {
            return ValueType.Int;
        }

        if (type.Struct is not { } structure)
        {
            Error("RUK2004", $"Cannot access field '{member.Name}' on {type}.", member);
            return ValueType.Error;
        }
        if (!structure.Fields.TryGetValue(member.Name, out var field))
        {
            Error("RUK2004", $"Unknown field '{member.Name}' on {type}.", member);
            return ValueType.Error;
        }
        Fields[member] = field;
        return field.Type;
    }

    private static bool IsVariablePath(Expression expression) => expression switch
    {
        Name => true,
        Member member => IsVariablePath(member.Receiver),
        Index index => IsVariablePath(index.Receiver),
        _ => false
    };

    private ValueType ResolveCall(Call call, Symbols scope)
    {
        if (call.TypeArgument is not null)
        {
            Error("RUK2005", "Explicit type arguments are supported only by designated built-in operations.", call);
        }
        if (call.Receiver is null && call.Name == "Error")
        {
            return Construct(call, ErrorStructure, scope);
        }
        // Dot syntax selects a module function for an import name, or a struct method for a value.
        Module? owner = module;
        FunctionSymbol? function = null;
        if (call.Receiver is Name name && module.Imports.TryGetValue(name.Token.Text, out var imported))
        {
            owner = imported;
        }
        else if (call.Receiver is not null)
        {
            var receiverType = TypeOf(call.Receiver, scope);
            if (receiverType == ValueType.Error)
            {
                return receiverType;
            }

            if (receiverType.DictionaryKey is not null)
                return DictionaryCallType(call, receiverType, scope);

            if (receiverType.Element is { } element && call.Name == "append")
            {
                RequireWritable(RootVariable(call.Receiver), call);
                if (!IsVariablePath(call.Receiver))
                {
                    Error("RUK2010", "Array append requires a variable, not a temporary value.", call.Receiver);
                }
                if (call.Arguments.Count != 1 || call.Arguments[0].Name is not null)
                {
                    Error("RUK2005", "append requires one positional argument.", call);
                }
                else
                {
                    Match(element, TypeOf(call.Arguments[0].Value, scope, element), call.Arguments[0].Value, "append value");
                }

                ArrayAppends.Add(call);
                return ValueType.Void;
            }

            if (receiverType.Struct is not { } structure || !structure.Methods.TryGetValue(call.Name, out function))
            {
                Error("RUK2004", $"Unknown method '{call.Name}' on {receiverType}.", call);
                return ValueType.Error;
            }
            owner = null;
        }
        if (owner is not null)
        {
            if (owner.Structs.TryGetValue(call.Name, out var structure))
            {
                return Construct(call, structure, scope);
            }

            if (owner.Functions.TryGetValue(call.Name, out var definition))
            {
                function = Functions[definition];
            }
        }
        if (function is null)
        {
            Error("RUK2004", $"Unknown function or struct '{call.Name}'.", call);
            return ValueType.Error;
        }
        Calls[call] = function;
        var signature = function.Function.Parameters;
        var arguments = call.Arguments
            .Select((argument, index) => TypeOf(
                argument.Value,
                scope,
                index < signature.Count ? DeclaredTypes[signature[index]] : null))
            .ToArray();
        if (call.Arguments.Any(a => a.Name is not null))
        {
            Error("RUK2005", "Function and method calls currently use positional arguments.", call);
        }

        if (arguments.Length != signature.Count)
        {
            Error("RUK2005", $"'{call.Name}' expects {signature.Count} arguments, got {arguments.Length}.", call);
        }

        for (var i = 0; i < Math.Min(arguments.Length, signature.Count); i++)
        {
            Match(DeclaredTypes[signature[i]], arguments[i], call.Arguments[i].Value, $"argument '{signature[i].Token.Text}'");
        }

        if (function.Module == module)
        {
            InvalidateGlobalRefinements();
        }

        return function.ReturnType;
    }

    private ValueType Construct(Call call, StructSymbol structure, Symbols scope)
    {
        if (!call.IsStructLiteral)
        {
            Error("RUK2005", $"Struct '{structure.Type}' literals use '{{ field: value }}' syntax.", call);
            return ValueType.Error;
        }
        Constructions[call] = structure;
        var supplied = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < call.Arguments.Count; i++)
        {
            var argument = call.Arguments[i];
            if (argument.Name is not { } name)
            {
                Error("RUK2005", $"Struct '{structure.Type}' requires named field arguments.", argument);
                continue;
            }
            if (!supplied.Add(name))
            {
                Error("RUK2002", $"Duplicate initializer for field '{name}'.", argument);
            }

            if (!structure.Fields.TryGetValue(name, out var field))
            {
                Error("RUK2004", $"Unknown field '{name}' on {structure.Type}.", argument);
                continue;
            }
            ConstructionFields[argument] = field;
            Match(field.Type, TypeOf(argument.Value, scope, field.Type), argument.Value, $"field '{name}'");
        }
        foreach (var field in structure.Fields.Keys.Where(f => !supplied.Contains(f)))
        {
            Error("RUK2005", $"Missing required field '{field}' for {structure.Type}.", call);
        }

        return structure.BoundType;
    }
}
