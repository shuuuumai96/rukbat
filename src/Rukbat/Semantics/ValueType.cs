namespace Rukbat;

// The AST retains type names until the checker binds them to their defining StructSymbol.
// Structs defined in different modules remain distinct even when their fields match.
internal sealed record ValueType(
    string Name,
    StructSymbol? Struct = null,
    ValueType? Element = null,
    ValueType? NullableElement = null,
    ValueType? DictionaryKey = null,
    ValueType? DictionaryValue = null)
{
    public static readonly ValueType Error = new("error");
    public static readonly ValueType Int = new("int");
    public static readonly ValueType Float = new("float");
    public static readonly ValueType String = new("string");
    public static readonly ValueType Bool = new("bool");
    public static readonly ValueType Void = new("void");
    public static readonly ValueType Null = new("null");
    public static readonly ValueType Encoding = new("encoding.Encoding");
    public static ValueType ArrayOf(ValueType element) => new(element.Name + "[]", Element: element);
    public static ValueType NullableOf(ValueType element) => new(element.Name + "?", NullableElement: element);
    public bool IsNullable => NullableElement is not null;
    public bool IsCsvRow => Struct?.IsCsvRow == true;
    public bool IsJsonValue => Struct?.IsJsonValue == true;
    public bool IsCliResult => Struct?.IsCliResult == true;
    public ValueType NonNullable => NullableElement ?? this;
    public bool IsComposite => NonNullable.Struct is not null || NonNullable.Element is not null || NonNullable.DictionaryKey is not null;
    public static ValueType DictionaryOf(ValueType key, ValueType value) => new($"dict[{key}, {value}]", DictionaryKey: key, DictionaryValue: value);
    public override string ToString() => Name;
}
