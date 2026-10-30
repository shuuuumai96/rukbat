namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private void EmitHttpLoader()
        {
            EmitNativeLoader("Http", "System.Net.Http");
            // Function parameters may use Body before the first network operation.
            Line("& $__rukbat_load_Http");
        }

        private string HttpCall(Call call, string operation)
        {
            var arguments = call.Arguments.Select(a => ",(" + HttpValue(Expr(a.Value), checker.Types[a.Value], false) + ")");
            return HttpValue($"(& $script:__rukbat_standard {Quote(operation)} @({string.Join("; ", arguments)}))", checker.Types[call], true);
        }

        // Adapt bound private field keys to the helper's ordered data, including nested
        // arrays and nullable values. No semantic/type lookup takes place at runtime.
        private string HttpValue(string expression, ValueType type, bool result)
        {
            if (type.NullableElement is { } nullable)
            {
                return "(& { $httpValue = " + expression + "; if ($null -eq $httpValue) { return $null }; return " + HttpValue("$httpValue", nullable, result) + " })";
            }

            if (type.Element is { } element)
            {
                var item = HttpValue("$httpItem", element, result);
                return "(& { $httpItems = " + expression + "; $httpList = [System.Collections.Generic.List[object]]::new(); " +
                    "foreach ($httpItem in $httpItems) { $httpList.Add((" + item + ")) }; " +
                    (result ? "return ,$httpList" : "return ,($httpList.ToArray())") + " })";
            }
            if (type.Struct is { } structure)
            {
                var fields = structure.Fields.Values.ToArray();
                if (result)
                {
                    return "(& { $httpValue = " + expression + "; return @{" + string.Join("; ", fields.Select((field, i) =>
                        Quote(field.Key) + " = " + HttpValue($"$httpValue[{i}]", field.Type, true))) + "} })";
                }

                return "(& { $httpValue = " + expression + "; return ,([object[]]@(" + string.Join("; ", fields.Select(field =>
                    ",(" + HttpValue($"$httpValue[{Quote(field.Key)}]", field.Type, false) + ")")) + ")) })";
            }
            return expression;
        }
    }
}
