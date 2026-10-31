namespace Rukbat
{
    internal sealed partial class TypeChecker
    {
        public HashSet<Call> DictionaryCalls { get; } = new(ReferenceEqualityComparer.Instance);

        private ValueType DictionaryType(DictionaryLiteral dictionary, Symbols scope, ValueType? expected)
        {
            var key = expected?.NonNullable.DictionaryKey;
            var value = expected?.NonNullable.DictionaryValue;
            if (dictionary.Entries.Count == 0 && key is null)
            {
                Error("RUK2001", "An empty dictionary requires an explicit dictionary type from a declaration, argument or return.", dictionary);
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
                    Error("RUK2001", "Dictionary values require a storable type; null needs an explicit nullable value type.", entry.Value);
                }

                var literal = LiteralKey(entry.Key);
                if (literal is not null && !literalKeys.Add(literal))
                {
                    Error("RUK2002", $"Duplicate dictionary key '{literal}'.", entry.Key);
                }
            }
            if (key != ValueType.String && key != ValueType.Int && key != ValueType.Error)
            {
                Error("RUK2001", "Dictionary keys must be non-null string or int values.", dictionary);
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

        private ValueType DictionaryCallType(Call call, ValueType receiver, Symbols scope)
        {
            if (call.Name is not ("has" or "remove" or "keys"))
            {
                foreach (var argument in call.Arguments)
                {
                    TypeOf(argument.Value, scope);
                }

                Error("RUK2004", $"Unknown method '{call.Name}' on {receiver}.", call);
                return ValueType.Error;
            }

            if (!RequireCallParentheses(call))
            {
                return ValueType.Error;
            }

            var count = call.Name == "keys" ? 0 : 1;
            if (call.Arguments.Count != count || call.Arguments.Any(argument => argument.Name is not null))
            {
                Error("RUK2005", $"{call.Name} requires {count} positional arguments.", call);
            }

            foreach (var argument in call.Arguments)
            {
                Match(receiver.DictionaryKey!, TypeOf(argument.Value, scope, receiver.DictionaryKey), argument.Value, "dictionary key");
            }

            if (call.Name == "remove")
            {
                RequireWritable(RootVariable(call.Receiver!), call);
                if (!IsVariablePath(call.Receiver!))
                {
                    Error("RUK2010", "Dictionary remove requires a variable, not a temporary value.", call.Receiver!);
                }
            }
            DictionaryCalls.Add(call);
            return call.Name switch
            {
                "keys" => ValueType.ArrayOf(receiver.DictionaryKey!),
                "has" => ValueType.Bool,
                _ => ValueType.Void
            };
        }
    }
}
