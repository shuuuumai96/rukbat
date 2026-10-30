namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private void EmitCliRuntime() => EmitLines(RuntimeResources.ReadPowerShell("CliHelpers"));

        private string CliCall(Call call, string operation)
        {
            var arguments = call.Arguments.Select(a => ",(" + CliValue(Expr(a.Value), checker.Types[a.Value], false) + ")");
            return CliValue($"(& $script:__rukbat_standard {Quote(operation)} @({string.Join("; ", arguments)}))", checker.Types[call], true);
        }

        // Translate public definition fields once at the boundary, using bound field keys.
        // Result storage is opaque and immutable; only get_all returns a copied Rukbat array.
        private string CliValue(string expression, ValueType type, bool result)
        {
            if (type.NullableElement is { } nullable)
            {
                return "(& { $cliValue = " + expression + "; if ($null -eq $cliValue) { return $null }; return " + CliValue("$cliValue", nullable, result) + " })";
            }

            if (type.Element is { } element)
            {
                var item = CliValue("$cliItem", element, result);
                return "(& { $cliItems = " + expression + "; $cliList = [System.Collections.Generic.List[object]]::new(); " +
                    "foreach ($cliItem in $cliItems) { $cliList.Add((" + item + ")) }; " +
                    (result ? "return ,$cliList" : "return ,($cliList.ToArray())") + " })";
            }
            return type.Struct is { } structure && !type.IsCliResult
                ? "(& { $cliValue = " + expression + "; return @{" + string.Join("; ", structure.Fields.Select(field =>
                    Quote(result ? field.Value.Key : field.Key) + " = " +
                    CliValue("$cliValue[" + Quote(result ? field.Key : field.Value.Key) + "]", field.Value.Type, result))) + "} })"
                : expression;
        }
    }
}
