namespace Rukbat
{
    internal sealed partial class TypeChecker
    {
        private static readonly IReadOnlyDictionary<string, (CallKind Kind, Func<TypeChecker, Call, ValueType[], ValueType> Check)> BuiltinCalls =
            new Dictionary<string, (CallKind, Func<TypeChecker, Call, ValueType[], ValueType>)>(StringComparer.Ordinal)
            {
                ["args"] = (CallKind.Arguments, static (checker, call, arguments) => checker.ArgumentsCallType(call, arguments)),
                ["print"] = (CallKind.Print, static (checker, call, arguments) => checker.PrintCallType(call, arguments)),
                ["eprint"] = (CallKind.PrintError, static (checker, call, arguments) => checker.PrintCallType(call, arguments)),
                ["exit"] = (CallKind.Exit, static (checker, call, arguments) => checker.ExitCallType(call, arguments))
            };

        private ValueType CallType(Call call, Symbols scope)
        {
            if (call.Receiver is Name standardAlias &&
                module.Imports.TryGetValue(standardAlias.Token.Text, out var standardModule) &&
                standardModule.StandardName is { } standardName)
            {
                if (call.IsStructLiteral && (standardName is "process" or "zip" or "http" or "cli" or "log" or "re") && standardModule.Structs.TryGetValue(call.Name, out var structure) && !structure.IsCliResult)
                {
                    return Construct(call, structure, scope);
                }

                return StandardCallType(call, scope, standardModule, standardName);
            }
            if (call.PowerShellType is { } powerShellType)
            {
                Calls[call] = new(CallKind.PowerShell);
                return PowerShellCallType(call, scope, powerShellType);
            }
            if (call.Receiver is null && BuiltinCalls.TryGetValue(call.Name, out var builtin))
            {
                Calls[call] = new(builtin.Kind);
                var arguments = call.Arguments.Select(argument => TypeOf(argument.Value, scope)).ToArray();
                return builtin.Check(this, call, arguments);
            }

            return ResolveCall(call, scope);
        }

        private ValueType ArgumentsCallType(Call call, ValueType[] arguments)
        {
            if (arguments.Length != 0)
            {
                Error(DiagnosticCode.InvalidCall, "args requires no arguments.", call);
            }
            return ValueType.ArrayOf(ValueType.String);
        }

        private ValueType ExitCallType(Call call, ValueType[] arguments)
        {
            if (arguments.Length != 1 || call.Arguments.Any(argument => argument.Name is not null))
            {
                Error(DiagnosticCode.InvalidCall, "exit requires one positional argument.", call);
            }
            else
            {
                Match(ValueType.Int, arguments[0], call.Arguments[0].Value);
            }
            return ValueType.Void;
        }

        private ValueType PowerShellCallType(Call call, Symbols scope, ValueType powerShellType)
        {
            var arguments = call.Arguments.Select(argument => TypeOf(argument.Value, scope)).ToArray();
            powerShellType = BindType(powerShellType, call);
            if (ContainsType(powerShellType, value => value.DictionaryKey is not null))
            {
                Error(DiagnosticCode.InvalidOperation, "Dictionaries cannot be returned directly from PowerShell. Convert primitive results explicitly.", call);
            }

            if (ContainsType(powerShellType, value => value.Enum is not null))
            {
                Error(DiagnosticCode.InvalidOperation, "PowerShell result types cannot contain enums. Convert returned text explicitly.", call);
            }

            if (ContainsOpaqueStandardValue(powerShellType))
            {
                Error(DiagnosticCode.InvalidOperation, "PowerShell result types cannot contain re.Pattern, fs.Lines, json.Value, csv.Row, cli.Result, encoding.Encoding, hash.Algorithm, log.Level, datetime types, http.Method or http.Body. Read text and use the corresponding parser.", call);
            }

            if (arguments.Length == 0 || call.Arguments[0].Name is not null)
            {
                Error(DiagnosticCode.InvalidCall, "powershell<T> requires a command name as its first positional argument.", call);
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
                {
                    Error(DiagnosticCode.InvalidOperation, "Dictionaries cannot be passed directly to PowerShell. Pass primitive values.", argument);
                }

                if (ContainsType(arguments[i], value => value.Enum is not null))
                {
                    Error(DiagnosticCode.InvalidOperation, "Enums cannot be passed directly to PowerShell. Convert them to text explicitly.", argument);
                }

                if (ContainsType(arguments[i], StandardValueTypes.IsOpaqueScalar))
                {
                    Error(DiagnosticCode.InvalidOperation, "Use explicit standard-library conversions before passing opaque values to PowerShell.", argument);
                }

                if (ContainsStruct(arguments[i]))
                {
                    Error(DiagnosticCode.InvalidOperation, "Structs cannot be passed directly to PowerShell. Pass primitive fields.", argument);
                }

                if (arguments[i] == ValueType.Void)
                {
                    Error(DiagnosticCode.InvalidType, "A void result cannot be passed as an argument.", argument);
                }

                if (argument.Name is { } name)
                {
                    sawNamed = true;
                    if (!named.Add(name))
                    {
                        Error(DiagnosticCode.InvalidCall, $"Duplicate PowerShell parameter '{name}'.", argument);
                    }
                }
                else if (sawNamed)
                {
                    Error(DiagnosticCode.InvalidCall, "Positional arguments must precede named arguments.", argument);
                }
            }
            return powerShellType;
        }

        private ValueType PrintCallType(Call call, ValueType[] arguments)
        {
            if (arguments.Length != 1 || call.Arguments.Any(a => a.Name is not null))
            {
                Error(DiagnosticCode.InvalidCall, $"{call.Name} requires one positional argument.", call);
            }
            else if (StandardValueTypes.IsOpaqueScalar(arguments[0]) || ContainsType(arguments[0], value => value == ValueType.RegexPattern || value == LogValues.Level))
            {
                Error(DiagnosticCode.InvalidOperation, "Use an explicit standard-library conversion before printing this value.", call);
            }
            else if (arguments[0].IsComposite)
            {
                Error(DiagnosticCode.InvalidOperation, "Print individual struct fields or array elements rather than the composite value.", call);
            }
            else if (arguments[0].IsNullable)
            {
                Error(DiagnosticCode.InvalidType, "A nullable value must be checked against null before it can be printed.", call.Arguments[0].Value);
            }
            else if (arguments[0] == ValueType.Void)
            {
                Error(DiagnosticCode.InvalidType, "Cannot print a void result.", call);
            }

            return ValueType.Void;
        }

        private bool RequireCallParentheses(Call call)
        {
            if (!call.IsStructLiteral)
            {
                return true;
            }

            Error(DiagnosticCode.InvalidCall, "Function and method calls use '(...)' syntax; braces are reserved for struct literals.", call);
            return false;
        }
    }
}
