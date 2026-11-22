namespace Rukbat
{
    internal sealed partial class Emitter
    {
        // Keep PowerShell's stream enumeration at the boundary. Internally a List retains spare capacity for append.
        private void EmitRuntime() => EmitLines(RuntimeResources.ReadPowerShell("CoreRuntime"));

        private static readonly IReadOnlyDictionary<ValueKind, Func<ValueType, string>> SchemaEmitters =
            new Dictionary<ValueKind, Func<ValueType, string>>
            {
                [ValueKind.Dictionary] = static type => EmitSchema("dictionary", type,
                    $"; key = {Quote(type.DictionaryKey!.Name)}; element = {Schema(type.DictionaryValue!)}"),
                [ValueKind.Nullable] = static type => EmitSchema("nullable", type, $"; element = {Schema(type.NullableElement!)}"),
                [ValueKind.Array] = static type => EmitSchema("array", type, $"; element = {Schema(type.Element!)}"),
                [ValueKind.CsvRow] = static type => EmitSchema("csv_row", type),
                [ValueKind.CliResult] = static type => EmitSchema("cli_result", type),
                [ValueKind.JsonValue] = static type => EmitSchema("json", type),
                [ValueKind.Struct] = EmitStructSchema,
                [ValueKind.Enum] = static type => EmitSchema("enum", type, $"; members = {EnumNames(type.Enum!)}"),
                [ValueKind.Scalar] = static type => EmitSchema(type.Name, type)
            };

        private static string Schema(ValueType type) => SchemaEmitters[type.Kind](type);

        private static string EmitSchema(string kind, ValueType type, string attributes = "") =>
            $"@{{kind = {Quote(kind)}; name = {Quote(type.Name)}{attributes}}}";

        private static string EmitStructSchema(ValueType type)
        {
            var fields = type.Struct!.Fields.Select(field =>
                $"@{{name = {Quote(field.Key)}; key = {Quote(field.Value.Key)}; type = {Schema(field.Value.Type)}}}");
            return EmitSchema("struct", type, $"; fields = @({string.Join("; ", fields)})");
        }

        private static string EnumNames(EnumSymbol enumeration) =>
            $"[string[]]@({string.Join(", ", enumeration.Members.Keys.Select(Quote))})";
    }
}
