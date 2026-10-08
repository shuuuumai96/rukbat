namespace Rukbat;

internal sealed partial class Emitter
{
    // Keep PowerShell's stream enumeration at the boundary. Internally a List retains spare capacity for append.
    private void EmitRuntime()
    {
        var runtime = RuntimeResources.ReadPowerShell("CoreRuntime");
        foreach (var line in runtime.Split('\n'))
        {
            Line(line.TrimEnd('\r'));
        }
    }

    private static string Schema(ValueType type)
    {
        if (type.DictionaryKey is { } key)
            return $"@{{kind = 'dictionary'; name = {Quote(type.Name)}; key = {Quote(key.Name)}; element = {Schema(type.DictionaryValue!)}}}";
        if (type.NullableElement is { } nullable)
        {
            return $"@{{kind = 'nullable'; name = {Quote(type.Name)}; element = {Schema(nullable)}}}";
        }
        if (type.Element is { } element)
        {
            return $"@{{kind = 'array'; name = {Quote(type.Name)}; element = {Schema(element)}}}";
        }

        if (type.IsCsvRow) return "@{kind = 'csv_row'; name = 'csv.Row'}";
        if (type.IsCliResult) return "@{kind = 'cli_result'; name = 'cli.Result'}";
        if (type.IsJsonValue)
        {
            return "@{kind = 'json'; name = 'json.Value'}";
        }
        if (type.Struct is { } structure)
        {
            var fields = structure.Fields.Select(f =>
                $"@{{name = {Quote(f.Key)}; key = {Quote(f.Value.Key)}; type = {Schema(f.Value.Type)}}}");
            return $"@{{kind = 'struct'; name = {Quote(type.Name)}; fields = @({string.Join("; ", fields)})}}";
        }
        return $"@{{kind = {Quote(type.Name)}; name = {Quote(type.Name)}}}";
    }
}
