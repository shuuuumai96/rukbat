namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private enum StandardFieldLayout
        {
            Named, Positional
        }

        private string AdaptedStandardCall(Call call, string operation, string prefix, StandardFieldLayout layout)
        {
            var arguments = call.Arguments.Select(argument =>
                ",(" + StandardValue(Expr(argument.Value), checker.Types[argument.Value], false, prefix, layout) + ")");
            var invocation = $"(& $script:__rukbat_standard {Quote(operation)} @({string.Join("; ", arguments)}))";
            return StandardValue(invocation, checker.Types[call], true, prefix, layout);
        }

        // Both boundaries traverse the same bound value shapes. Only struct storage
        // differs: CLI uses public field names, HTTP uses ordered object arrays.
        private string StandardValue(string expression, ValueType type, bool result, string prefix, StandardFieldLayout layout)
        {
            var value = "$" + prefix + "Value";
            if (type.NullableElement is { } nullable)
            {
                return $"(& {{ {value} = {expression}; if ($null -eq {value}) {{ return $null }}; return " +
                    StandardValue(value, nullable, result, prefix, layout) + " })";
            }

            if (type.Element is { } element)
            {
                var items = "$" + prefix + "Items";
                var list = "$" + prefix + "List";
                var item = "$" + prefix + "Item";
                var adapted = StandardValue(item, element, result, prefix, layout);
                return $"(& {{ {items} = {expression}; {list} = [System.Collections.Generic.List[object]]::new(); " +
                    $"foreach ({item} in {items}) {{ {list}.Add(({adapted})) }}; " +
                    (result ? $"return ,{list}" : $"return ,({list}.ToArray())") + " })";
            }

            if (type.Struct is not { } structure || type.IsCliResult)
            {
                // Native helpers cast their object-array slots directly. Unwrap imported
                // scalar results before placing them in adapted arrays and struct fields.
                return !result && type.Struct is null ? $"([{TypeName(type)}]({expression}))" : expression;
            }

            var fields = structure.Fields.ToArray();
            if (!result && layout == StandardFieldLayout.Positional)
            {
                var entries = fields.Select(field => ",(" +
                    StandardValue($"{value}[{Quote(field.Value.Key)}]", field.Value.Type, false, prefix, layout) + ")");
                return $"(& {{ {value} = {expression}; return ,([object[]]@(" + string.Join("; ", entries) + ")) })";
            }

            var assignments = fields.Select((field, index) =>
            {
                var source = layout == StandardFieldLayout.Positional ? index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : Quote(result ? field.Key : field.Value.Key);
                var target = Quote(result ? field.Value.Key : field.Key);
                return target + " = " + StandardValue($"{value}[{source}]", field.Value.Type, result, prefix, layout);
            });
            return $"(& {{ {value} = {expression}; return @{{" + string.Join("; ", assignments) + "} })";
        }
    }
}
