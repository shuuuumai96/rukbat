namespace Rukbat;

internal sealed partial class TypeChecker
{
    private static bool IsNumber(ValueType type) => type == ValueType.Int || type == ValueType.Float;

    private static bool CanConvert(ValueType expected, ValueType actual)
    {
        if (expected == actual || expected == ValueType.Error || actual == ValueType.Error)
        {
            return true;
        }
        if (expected.IsNullable)
        {
            if (actual == ValueType.Null)
            {
                return true;
            }
            return actual.IsNullable
                ? CanConvert(expected.NonNullable, actual.NonNullable)
                : CanConvert(expected.NonNullable, actual);
        }
        return false;
    }

    private void Match(ValueType expected, ValueType actual, Node node, string? context = null)
    {
        if (CanConvert(expected, actual))
        {
            return;
        }

        var expectedName = expected.Name;
        var actualName = actual.Name;
        if (expectedName == actualName && expected.IsComposite && actual.IsComposite)
        {
            expectedName = QualifiedTypeName(expected);
            actualName = QualifiedTypeName(actual);
        }
        Error("RUK2001", $"Expected {expectedName}{(context is null ? "" : " for " + context)}, got {actualName}.", node);
    }

    private static string QualifiedTypeName(ValueType type)
    {
        if (type.DictionaryKey is { } key)
            return $"dict[{QualifiedTypeName(key)}, {QualifiedTypeName(type.DictionaryValue!)}]";
        if (type.NullableElement is { } nullable)
        {
            return QualifiedTypeName(nullable) + "?";
        }
        if (type.Element is { } element)
        {
            return QualifiedTypeName(element) + "[]";
        }

        if (type.Struct is { } structure)
        {
            return Path.GetFileNameWithoutExtension(structure.Module.Source.Path) + "." + type.Name;
        }

        return type.Name;
    }
}
