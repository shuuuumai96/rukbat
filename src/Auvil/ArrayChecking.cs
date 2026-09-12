namespace Auvil;

internal sealed partial class TypeChecker
{
    private static bool ContainsStruct(ValueType type) =>
        type.Struct is not null || type.Element is { } element && ContainsStruct(element);

    private ValueType ArrayType(ArrayLiteral array, Symbols scope, ValueType? expected)
    {
        // Empty arrays need an element type from the destination or parameter declaration.
        var element = expected?.Element;
        if (element is null && array.Items.Count == 0)
        {
            Error("AUV2001", "An empty array requires an explicit array type from a declaration, argument or return.", array);
            return ValueType.Error;
        }
        foreach (var item in array.Items)
        {
            var actual = TypeOf(item, scope, element);
            element ??= actual;
            if (actual == ValueType.Void)
            {
                Error("AUV2001", "Array elements cannot have type void.", item);
            }

            Match(element, actual, item, "array element");
        }
        return ValueType.ArrayOf(element!);
    }

    private ValueType IndexType(Index index, Symbols scope)
    {
        var receiver = TypeOf(index.Receiver, scope);
        Match(ValueType.Int, TypeOf(index.Offset, scope), index.Offset, "array index");
        if (receiver.Element is { } element)
        {
            return element;
        }

        if (receiver != ValueType.Error)
        {
            Error("AUV2001", $"Cannot index {receiver}; expected an array.", index);
        }

        return ValueType.Error;
    }
}
