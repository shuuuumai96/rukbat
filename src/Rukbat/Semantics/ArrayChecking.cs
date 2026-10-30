namespace Rukbat
{
    internal sealed partial class TypeChecker
    {
        private ValueType ArrayCallType(Call call, Symbols scope)
        {
            if (call.Name is not ("slice" or "remove_at" or "sort" or "sort_descending" or "sort_by" or "sort_by_descending"))
            {
                Error("RUK2004", $"Unknown standard-library function 'array.{call.Name}'.", call);
                return ValueType.Error;
            }
            int count = call.Name == "slice" ? 3 : call.Name is "sort" or "sort_descending" ? 1 : 2;
            if (call.TypeArgument is not null || call.Arguments.Count != count || call.Arguments.Any(argument => argument.Name is not null))
            {
                Error("RUK2005", $"array.{call.Name} expects {count} positional arguments and infers its types.", call);
            }

            var types = call.Arguments.Select(argument => TypeOf(argument.Value, scope)).ToArray();
            if (types.Length == 0)
            {
                return ValueType.Error;
            }

            var values = types[0];
            if (values.Element is null && values != ValueType.Error)
            {
                Error("RUK2001", "Array operations require a non-null array; empty literals need a declared element type.", call.Arguments[0]);
            }

            if (call.Name is "slice" or "remove_at")
            {
                for (int i = 1; i < types.Length; i++)
                {
                    Match(ValueType.Int, types[i], call.Arguments[i]);
                }
            }
            else
            {
                var keys = call.Name is "sort_by" or "sort_by_descending" ? types.ElementAtOrDefault(1)?.Element : values.Element;
                if (keys is not null && keys != ValueType.Error && keys != ValueType.Int && keys != ValueType.Float && keys != ValueType.String && !DateTimeValues.Ordered(keys))
                {
                    Error("RUK2001", "Sort keys must be non-null int, float, string, datetime.Timestamp, datetime.Date or datetime.Duration values.", call);
                }

                if (call.Name is "sort_by" or "sort_by_descending" && types.Length > 1 && types[1].Element is null && types[1] != ValueType.Error)
                {
                    Error("RUK2001", "Sort keys must be a non-null array.", call.Arguments[1]);
                }
            }
            StandardCalls[call] = "array." + call.Name;
            return values.Element is null ? ValueType.Error : values;
        }

        private static bool ContainsStruct(ValueType type) =>
            type.NullableElement is { } nullable && ContainsStruct(nullable) ||
            type.Struct is not null || type.Element is { } element && ContainsStruct(element) ||
            type.DictionaryValue is { } value && ContainsStruct(value);

        private ValueType ArrayType(ArrayLiteral array, Symbols scope, ValueType? expected)
        {
            // Empty arrays need an element type from the destination or parameter declaration.
            var element = expected?.NonNullable.Element;
            if (element is null && array.Items.Count == 0)
            {
                Error("RUK2001", "An empty array requires an explicit array type from a declaration, argument or return.", array);
                return ValueType.Error;
            }
            foreach (var item in array.Items)
            {
                var actual = TypeOf(item, scope, element);
                element ??= actual;
                if (actual == ValueType.Void)
                {
                    Error("RUK2001", "Array elements cannot have type void.", item);
                }

                Match(element, actual, item, "array element");
            }
            return ValueType.ArrayOf(element!);
        }

        private ValueType IndexType(Index index, Symbols scope)
        {
            var receiver = TypeOf(index.Receiver, scope);
            if (receiver.DictionaryKey is { } key)
            {
                Match(key, TypeOf(index.Offset, scope, key), index.Offset, "dictionary key");
                return receiver.DictionaryValue!;
            }
            Match(ValueType.Int, TypeOf(index.Offset, scope), index.Offset, "array index");
            if (receiver.Element is { } element)
            {
                return element;
            }

            if (receiver != ValueType.Error)
            {
                Error("RUK2001", $"Cannot index {receiver}; expected an array.", index);
            }

            return ValueType.Error;
        }
    }
}
