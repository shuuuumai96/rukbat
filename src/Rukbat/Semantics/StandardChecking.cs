namespace Rukbat
{
    internal sealed partial class TypeChecker
    {
        private ValueType StandardCallType(Call call, Symbols scope, Module standardModule, string moduleName)
        {
            if (moduleName == "array")
            {
                return ArrayCallType(call, scope);
            }

            if (moduleName == "json" && call.Name is "stringify" or "parse" or "as")
            {
                if (!RequireCallParentheses(call))
                {
                    return ValueType.Error;
                }

                return JsonCallType(call, scope, standardModule.Structs["Value"].BoundType);
            }

            if (moduleName == "json" && call.Name is "set" or "set_at" or "append")
            {
                if (!RequireCallParentheses(call))
                {
                    return ValueType.Error;
                }

                return JsonUpdateCallType(call, scope, standardModule.Structs["Value"].BoundType);
            }

            var signature = StandardSignatures.Find(standardModule, call.Name);
            if (signature is null)
            {
                Error("RUK2004", $"Unknown standard-library function '{moduleName}.{call.Name}'.", call);
                return ValueType.Error;
            }

            if (!RequireCallParentheses(call))
            {
                return ValueType.Error;
            }

            if (call.TypeArgument is not null)
            {
                Error("RUK2005", $"{moduleName}.{call.Name} does not accept a type argument.", call);
            }
            CheckStandardArguments(call, scope, signature.Parameters, $"{moduleName}.{call.Name}");
            StandardCalls[call] = $"{moduleName}.{call.Name}";
            return signature.Result;
        }

        private void CheckStandardArguments(Call call, Symbols scope, IReadOnlyList<ValueType> parameters, string name)
        {
            if (call.Arguments.Count != parameters.Count)
            {
                Error("RUK2005", $"{name} expects {parameters.Count} positional arguments, got {call.Arguments.Count}.", call);
            }
            if (call.Arguments.Any(argument => argument.Name is not null))
            {
                Error("RUK2005", $"{name} accepts positional arguments only. Remove the argument names.", call);
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
    }
}
