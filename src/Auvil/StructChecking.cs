namespace Auvil;

internal sealed class StructSymbol(Module module, StructDeclaration declaration)
{
    public Module Module { get; } = module;
    public StructDeclaration Declaration { get; } = declaration;
    public ValueType Type { get; } = new(declaration.Token.Text);
    public Dictionary<string, FieldSymbol> Fields { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, FunctionSymbol> Methods { get; } = new(StringComparer.Ordinal);
    public ValueType BoundType => Type with { Struct = this };
}
internal sealed record FieldSymbol(Field Field, ValueType Type, string Key);

internal sealed partial class TypeChecker
{
    public Dictionary<Node, ValueType> DeclaredTypes { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Member, FieldSymbol> Fields { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Call, StructSymbol> Constructions { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<Argument, FieldSymbol> ConstructionFields { get; } = new(ReferenceEqualityComparer.Instance);

    private void RegisterDefinitions(List<Module> modules)
    {
        foreach (var current in modules)
        {
            module = current;
            foreach (var declaration in current.Statements.OfType<StructDeclaration>())
            {
                var name = declaration.Token.Text;
                if (!current.Structs.TryAdd(name, new(current, declaration))) Error("AUV2002", $"Duplicate struct '{name}'.", declaration);
                if (IsPrimitive(name) || current.Imports.ContainsKey(name)) Error("AUV2002", $"Struct name '{name}' conflicts with an existing type or import.", declaration);
            }
            foreach (var function in current.Statements.OfType<Function>())
            {
                if (!current.Functions.TryAdd(function.Name, function)) Error("AUV2002", $"Duplicate function '{function.Name}'.", function);
                if (current.Structs.ContainsKey(function.Name)) Error("AUV2002", $"Function '{function.Name}' conflicts with a struct.", function);
                Functions[function] = new(current, function, $"__auv_f{Functions.Count}");
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
                    if (type == ValueType.Void) Error("AUV2001", $"Field '{field.Token.Text}' cannot have type void.", field);
                    var symbol = new FieldSymbol(field, type, $"__auv_field{structure.Fields.Count}");
                    if (!structure.Fields.TryAdd(field.Token.Text, symbol)) Error("AUV2002", $"Duplicate field '{field.Token.Text}'.", field);
                }
                foreach (var method in structure.Declaration.Methods)
                {
                    var symbol = new FunctionSymbol(current, method, $"__auv_f{Functions.Count}", structure);
                    Functions[method] = symbol;
                    if (!structure.Methods.TryAdd(method.Name, symbol)) Error("AUV2002", $"Duplicate method '{method.Name}'.", method);
                    if (structure.Fields.ContainsKey(method.Name)) Error("AUV2002", $"Method '{method.Name}' conflicts with a field.", method);
                }
            }
        }
        foreach (var function in Functions.Values)
        {
            module = function.Module;
            function.ReturnType = BindType(function.Function.Type, function.Function);
            foreach (var parameter in function.Function.Parameters) BindType(parameter.Type, parameter);
        }
        // Direct recursion cannot form finite values. Array-mediated recursion is also
        // deferred until recursive type schemas and runtime conversion have explicit support.
        var visited = new HashSet<StructSymbol>(); var active = new HashSet<StructSymbol>();
        foreach (var structure in modules.SelectMany(m => m.Structs.Values)) CheckFinite(structure, visited, active);
    }
    private static bool IsPrimitive(string name) => name is "int" or "string" or "bool" or "void";
    private ValueType BindType(ValueType type, Node node)
    {
        if (type.Element is { } element)
        {
            var bound = BindType(element, node);
            if (bound == ValueType.Void) Error("AUV2001", "Array elements cannot have type void.", node);
            return DeclaredTypes[node] = ValueType.ArrayOf(bound);
        }
        if (IsPrimitive(type.Name)) return DeclaredTypes[node] = type;
        var parts = type.Name.Split('.');
        var owner = parts.Length == 1 ? module : module.Imports.GetValueOrDefault(parts[0]);
        if (owner is not null && owner.Structs.TryGetValue(parts[^1], out var structure))
            return DeclaredTypes[node] = structure.BoundType;
        Error("AUV2004", $"Unknown struct/type '{type.Name}'.", node);
        return DeclaredTypes[node] = ValueType.Error;
    }
    private static void CheckFinite(StructSymbol structure, HashSet<StructSymbol> visited, HashSet<StructSymbol> active)
    {
        if (visited.Contains(structure)) return;
        active.Add(structure);
        foreach (var field in structure.Fields.Values)
        {
            var fieldType = field.Type;
            while (fieldType.Element is { } element) fieldType = element;
            if (fieldType.Struct is not { } nested) continue;
            if (active.Contains(nested)) structure.Module.Source.Error("AUV2010", $"Recursive value field '{field.Field.Token.Text}' is not supported.", field.Field.Token);
            else CheckFinite(nested, visited, active);
        }
        active.Remove(structure); visited.Add(structure);
    }
    private ValueType MemberType(Member member, Symbols scope)
    {
        var type = TypeOf(member.Receiver, scope);
        if (type == ValueType.Error) return type;
        if (type.Element is not null && member.Name == "length") return ValueType.Int;
        if (type.Struct is not { } structure)
        { Error("AUV2004", $"Cannot access field '{member.Name}' on {type}.", member); return ValueType.Error; }
        if (!structure.Fields.TryGetValue(member.Name, out var field))
        { Error("AUV2004", $"Unknown field '{member.Name}' on {type}.", member); return ValueType.Error; }
        Fields[member] = field;
        return field.Type;
    }
    private static bool IsVariablePath(Expression expression) => expression is Name || expression is Member member && IsVariablePath(member.Receiver) || expression is Index index && IsVariablePath(index.Receiver);

    private ValueType ResolveCall(Call call, Symbols scope)
    {
        Module? owner = module;
        FunctionSymbol? function = null;
        if (call.Receiver is Name name && module.Imports.TryGetValue(name.Token.Text, out var imported)) owner = imported;
        else if (call.Receiver is not null)
        {
            var receiverType = TypeOf(call.Receiver, scope);
            if (receiverType == ValueType.Error) return receiverType;
            if (receiverType.Struct is not { } structure || !structure.Methods.TryGetValue(call.Name, out function))
            { Error("AUV2004", $"Unknown method '{call.Name}' on {receiverType}.", call); return ValueType.Error; }
            owner = null;
        }
        if (owner is not null)
        {
            if (owner.Structs.TryGetValue(call.Name, out var structure)) return Construct(call, structure, scope);
            if (owner.Functions.TryGetValue(call.Name, out var definition)) function = Functions[definition];
        }
        if (function is null)
        { Error("AUV2004", $"Unknown function or struct '{call.Name}'.", call); return ValueType.Error; }
        Calls[call] = function;
        var signature = function.Function.Parameters;
        var arguments = call.Arguments.Select((a, i) => TypeOf(a.Value, scope, i < signature.Count ? DeclaredTypes[signature[i]] : null)).ToArray();
        if (call.Arguments.Any(a => a.Name is not null)) Error("AUV2005", "Function and method calls currently use positional arguments.", call);
        if (arguments.Length != signature.Count) Error("AUV2005", $"'{call.Name}' expects {signature.Count} arguments, got {arguments.Length}.", call);
        for (var i = 0; i < Math.Min(arguments.Length, signature.Count); i++) Match(DeclaredTypes[signature[i]], arguments[i], call.Arguments[i], $"argument '{signature[i].Token.Text}'");
        return function.ReturnType;
    }
    private ValueType Construct(Call call, StructSymbol structure, Symbols scope)
    {
        Constructions[call] = structure;
        var supplied = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < call.Arguments.Count; i++)
        {
            var argument = call.Arguments[i];
            if (argument.Name is not { } name)
            { Error("AUV2005", $"Struct '{structure.Type}' requires named field arguments.", argument); continue; }
            if (!supplied.Add(name)) Error("AUV2002", $"Duplicate initializer for field '{name}'.", argument);
            if (!structure.Fields.TryGetValue(name, out var field))
            { Error("AUV2004", $"Unknown field '{name}' on {structure.Type}.", argument); continue; }
            ConstructionFields[argument] = field;
            Match(field.Type, TypeOf(argument.Value, scope, field.Type), argument, $"field '{name}'");
        }
        foreach (var field in structure.Fields.Keys.Where(f => !supplied.Contains(f)))
            Error("AUV2005", $"Missing required field '{field}' for {structure.Type}.", call);
        return structure.BoundType;
    }
}
