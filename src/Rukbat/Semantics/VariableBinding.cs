namespace Rukbat;

internal sealed partial class TypeChecker
{
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

        var symbol = new VariableSymbol(name, type, $"${(global ? "script:" : "")}__rukbat_v{variableId++}", global,
            node is Variable { IsConst: true });
        if (!scope.Variables.TryAdd(name, symbol))
        {
            Error("RUK2002", $"Duplicate declaration '{name}'.", node);
        }

        Variables[node] = symbol;
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
}
