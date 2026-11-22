namespace Rukbat
{
    internal sealed partial class TypeChecker
    {

        private ValueType DictionaryType(DictionaryLiteral dictionary, Symbols scope, ValueType? expected)
        {
            var key = expected?.NonNullable.DictionaryKey;
            var value = expected?.NonNullable.DictionaryValue;
            if (dictionary.Entries.Count == 0 && key is null)
            {
                Error(DiagnosticCode.InvalidType, "An empty dictionary requires an explicit dictionary type from a declaration, argument or return.", dictionary);
                return ValueType.Error;
            }
            var literalKeys = new HashSet<object>();
            foreach (var entry in dictionary.Entries)
            {
                var actualKey = TypeOf(entry.Key, scope, key);
                var actualValue = TypeOf(entry.Value, scope, value);
                key ??= actualKey;
                value ??= actualValue;
                Match(key, actualKey, entry.Key, "dictionary key");
                Match(value, actualValue, entry.Value, "dictionary value");
                if (value == ValueType.Void || value == ValueType.Null)
                {
                    Error(DiagnosticCode.InvalidType, "Dictionary values require a storable type; null needs an explicit nullable value type.", entry.Value);
                }

                var literal = LiteralKey(entry.Key);
                if (literal is not null && !literalKeys.Add(literal))
                {
                    Error(DiagnosticCode.ConflictingName, $"Duplicate dictionary key '{literal}'.", entry.Key);
                }
            }
            if (key != ValueType.String && key != ValueType.Int && key != ValueType.Error)
            {
                Error(DiagnosticCode.InvalidType, "Dictionary keys must be non-null string or int values.", dictionary);
            }

            return ValueType.DictionaryOf(key!, value!);
        }

        private static object? LiteralKey(Expression expression) => expression switch
        {
            Literal { Value: string or long } literal => literal.Value,
            Unary { Token.Text: "-", Value: Literal { Value: long value } } when value != long.MinValue => -value,
            Unary { Token.Text: "+", Value: Literal { Value: long value } } => value,
            _ => null
        };

        private static readonly IReadOnlyDictionary<string, (int Arguments, bool Mutates, Func<ValueType, ValueType> Result)> DictionaryMethods =
            new Dictionary<string, (int, bool, Func<ValueType, ValueType>)>(StringComparer.Ordinal)
            {
                [ContainerMember.Has] = (1, false, static _ => ValueType.Bool),
                [ContainerMember.Remove] = (1, true, static _ => ValueType.Void),
                [ContainerMember.Keys] = (0, false, ValueType.ArrayOf)
            };

        private ValueType DictionaryCallType(Call call, ValueType receiver, Symbols scope)
        {
            if (!DictionaryMethods.TryGetValue(call.Name, out var operation))
            {
                foreach (var argument in call.Arguments)
                {
                    TypeOf(argument.Value, scope);
                }

                Error(DiagnosticCode.UnknownName, $"Unknown method '{call.Name}' on {receiver}.", call);
                return ValueType.Error;
            }

            if (!RequireCallParentheses(call))
            {
                return ValueType.Error;
            }

            var count = operation.Arguments;
            if (call.Arguments.Count != count || call.Arguments.Any(argument => argument.Name is not null))
            {
                Error(DiagnosticCode.InvalidCall, $"{call.Name} requires {count} positional arguments.", call);
            }

            foreach (var argument in call.Arguments)
            {
                Match(receiver.DictionaryKey!, TypeOf(argument.Value, scope, receiver.DictionaryKey), argument.Value, "dictionary key");
            }

            if (operation.Mutates)
            {
                RequireWritable(RootVariable(call.Receiver!), call);
                if (!IsVariablePath(call.Receiver!))
                {
                    Error(DiagnosticCode.InvalidOperation, "Dictionary remove requires a variable, not a temporary value.", call.Receiver!);
                }
            }
            Calls[call] = new(CallKind.DictionaryMethod);
            return operation.Result(receiver.DictionaryKey!);
        }
    }
}
