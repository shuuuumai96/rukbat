namespace Rukbat;

internal sealed partial class TypeChecker
{
    private ValueType JsonCallType(Call call, Symbols scope, ValueType jsonValue)
    {
        if (call.Name is "parse" or "as")
        {
            CheckStandardArguments(call, scope, [call.Name == "parse" ? ValueType.String : jsonValue], $"json.{call.Name}");
            if (call.Name == "as" && call.TypeArgument is null)
            {
                Error("RUK2005", "json.as requires an explicit result type: json.as<T>(value).", call);
                return ValueType.Error;
            }
            var resultType = call.TypeArgument is null ? jsonValue : BindType(call.TypeArgument, call);
            if (!SupportsJson(resultType))
            {
                Error("RUK2010", $"Type {resultType} cannot be read from JSON.", call);
            }
            StandardCalls[call] = $"json.{call.Name}";
            return resultType;
        }
        if (call.Arguments.Count != 1 || call.Arguments.Any(argument => argument.Name is not null))
        {
            Error("RUK2005", $"json.{call.Name} requires one positional argument.", call);
        }
        var argumentTypes = call.Arguments.Select(argument => TypeOf(argument.Value, scope)).ToArray();
        var argumentType = argumentTypes.FirstOrDefault() ?? ValueType.Error;
        if (call.TypeArgument is not null)
        {
            Error("RUK2005", "json.stringify infers its input type and does not accept a type argument.", call);
        }
        if (!SupportsJson(argumentType))
        {
            Error("RUK2010", $"Type {argumentType} cannot be encoded as JSON.", call);
        }
        StandardCalls[call] = "json.stringify";
        return ValueType.String;
    }

    private ValueType JsonUpdateCallType(Call call, Symbols scope, ValueType jsonValue)
    {
        // A replacement accepts the same concrete types as stringify, plus literal null.
        // Check it separately so no arbitrary type is accepted in other argument positions.
        ValueType[] prefix = call.Name == "append" ? [jsonValue] :
            [jsonValue, call.Name == "set" ? ValueType.String : ValueType.Int];
        if (call.TypeArgument is not null)
            Error("RUK2005", $"json.{call.Name} infers its replacement type and does not accept a type argument.", call);
        if (call.Arguments.Count != prefix.Length + 1 || call.Arguments.Any(a => a.Name is not null))
            Error("RUK2005", $"json.{call.Name} expects {prefix.Length + 1} positional arguments.", call);
        for (var index = 0; index < call.Arguments.Count; index++)
        {
            var actual = TypeOf(call.Arguments[index].Value, scope);
            if (index < prefix.Length)
                Match(prefix[index], actual, call.Arguments[index].Value, $"argument {index + 1}");
            else if (actual != ValueType.Null && !SupportsJson(actual))
                Error("RUK2010", $"Type {actual} cannot be encoded as JSON.", call.Arguments[index].Value);
        }
        StandardCalls[call] = $"json.{call.Name}";
        return jsonValue;
    }

    private bool SupportsJson(ValueType type)
    {
        if (ContainsNonJsonStandardValue(type)) return false;
        if (type.IsNullable)
        {
            return SupportsJson(type.NonNullable);
        }
        if (type.Element is { } element)
        {
            return SupportsJson(element);
        }
        if (ContainsType(type, value => value.DictionaryKey is { } key && key != ValueType.String)) return false;
        if (type.DictionaryValue is { } dictionaryValue) return SupportsJson(dictionaryValue);
        return type is { Struct: not null } && type.Struct != ErrorStructure ||
            type == ValueType.Bool || type == ValueType.Int || type == ValueType.Float || type == ValueType.String;
    }

    private static bool ContainsNonJsonStandardValue(ValueType type) =>
        ContainsType(type, value => value == ValueType.FileLines || DateTimeValues.Contains(value) || HttpValues.Contains(value) || (value == ValueType.Encoding || value == HashValues.Algorithm) || value.IsCsvRow || value.IsCliResult);
    private static bool ContainsOpaqueStandardValue(ValueType type) =>
        ContainsType(type, value => value == ValueType.FileLines || DateTimeValues.Contains(value) || HttpValues.Contains(value) || (value == ValueType.Encoding || value == HashValues.Algorithm) || value.IsCsvRow || value.IsJsonValue || value.IsCliResult);
    private static bool ContainsDateTime(ValueType type) => ContainsType(type, DateTimeValues.Contains);
    private static bool ContainsTaggedStandardValue(ValueType type) => ContainsType(type, value => (value == ValueType.Encoding || value == HashValues.Algorithm));

    private static bool ContainsType(ValueType type, Func<ValueType, bool> matches)
    {
        var visited = new HashSet<StructSymbol>();
        bool Contains(ValueType value) => matches(value) ||
            value.NullableElement is { } nullable && Contains(nullable) ||
            value.Element is { } element && Contains(element) ||
            value.DictionaryValue is { } dictionaryValue && Contains(dictionaryValue) ||
            value.Struct is { } structure && visited.Add(structure) && structure.Fields.Values.Any(field => Contains(field.Type));
        return Contains(type);
    }
}
