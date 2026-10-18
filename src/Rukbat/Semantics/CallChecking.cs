namespace Rukbat;

internal sealed partial class TypeChecker
{
    private ValueType CallType(Call call, Symbols scope)
    {
        if (call.Receiver is Name standardAlias &&
            module.Imports.TryGetValue(standardAlias.Token.Text, out var standardModule) &&
            standardModule.StandardName is { } standardName)
        {
            if (call.IsStructLiteral && (standardName is "process" or "zip" or "http" or "cli" or "log" or "re") && standardModule.Structs.TryGetValue(call.Name, out var structure) && !structure.IsCliResult)
                return Construct(call, structure, scope);
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
            if (ContainsType(powerShellType, value => value.DictionaryKey is not null))
                Error("RUK2010", "Dictionaries cannot be returned directly from PowerShell. Convert primitive results explicitly.", call);
            if (ContainsOpaqueStandardValue(powerShellType))
            {
                Error("RUK2010", "PowerShell result types cannot contain re.Pattern, fs.Lines, json.Value, csv.Row, cli.Result, encoding.Encoding, hash.Algorithm, log.Level, datetime types, http.Method or http.Body. Read text and use the corresponding parser.", call);
            }

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
                if (ContainsType(arguments[i], value => value.DictionaryKey is not null))
                    Error("RUK2010", "Dictionaries cannot be passed directly to PowerShell. Pass primitive values.", argument);
                if (ContainsTaggedStandardValue(arguments[i]) || ContainsDateTime(arguments[i]) || ContainsType(arguments[i], value => value == ValueType.RegexPattern || value == ValueType.FileLines || HttpValues.Contains(value)))
                    Error("RUK2010", "Use explicit standard-library conversions before passing opaque values to PowerShell.", argument);
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
            else if (arguments[0] == ValueType.FileLines || arguments[0] == ValueType.Encoding || arguments[0] == HashValues.Algorithm || ContainsType(arguments[0], value => value == ValueType.RegexPattern || value == LogValues.Level) || DateTimeValues.Contains(arguments[0]) || HttpValues.Contains(arguments[0]))
            {
                Error("RUK2010", "Use an explicit standard-library conversion before printing this value.", call);
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
}
