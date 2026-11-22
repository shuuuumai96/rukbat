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
                ",(" + StandardValue(Expr(argument.Value), checker.Types[argument.Value], new(false, prefix, layout)) + ")");
            var invocation = $"(& $script:__rukbat_standard {Quote(operation)} @({string.Join("; ", arguments)}))";
            return StandardValue(invocation, checker.Types[call], new(true, prefix, layout));
        }

        private readonly record struct StandardValueContext(bool Result, string Prefix, StandardFieldLayout Layout)
        {
            public string Variable(string role) => "$" + Prefix + role;
        }

        private delegate string StandardValueStrategy(string expression, ValueType type, StandardValueContext context);

        private static readonly IReadOnlyDictionary<ValueKind, StandardValueStrategy> StandardValueStrategies =
            new Dictionary<ValueKind, StandardValueStrategy>
            {
                [ValueKind.Nullable] = AdaptNullableValue,
                [ValueKind.Array] = AdaptArrayValue,
                [ValueKind.Struct] = AdaptStructValue,
                [ValueKind.CsvRow] = AdaptStructValue,
                [ValueKind.JsonValue] = AdaptStructValue,
                [ValueKind.CliResult] = static (expression, _, _) => expression
            };

        private static string StandardValue(string expression, ValueType type, StandardValueContext context) =>
            StandardValueStrategies.GetValueOrDefault(type.Kind, AdaptScalarValue)(expression, type, context);

        private static string AdaptScalarValue(string expression, ValueType type, StandardValueContext context) =>
            context.Result ? expression : $"([{TypeName(type)}]({expression}))";

        private static string AdaptNullableValue(string expression, ValueType type, StandardValueContext context)
        {
            var value = context.Variable("Value");
            return $"(& {{ {value} = {expression}; if ($null -eq {value}) {{ return $null }}; return " +
                StandardValue(value, type.NullableElement!, context) + " })";
        }

        private static string AdaptArrayValue(string expression, ValueType type, StandardValueContext context)
        {
            var items = context.Variable("Items");
            var list = context.Variable("List");
            var item = context.Variable("Item");
            var adapted = StandardValue(item, type.Element!, context);
            return $"(& {{ {items} = {expression}; {list} = [System.Collections.Generic.List[object]]::new(); " +
                $"foreach ({item} in {items}) {{ {list}.Add(({adapted})) }}; " +
                (context.Result ? $"return ,{list}" : $"return ,({list}.ToArray())") + " })";
        }

        // CLI uses public field names. HTTP uses ordered object arrays.
        private static string AdaptStructValue(string expression, ValueType type, StandardValueContext context)
        {
            var value = context.Variable("Value");
            var (result, _, layout) = context;
            var fields = type.Struct!.Fields;
            if (!result && layout == StandardFieldLayout.Positional)
            {
                var entries = fields.Select(field => ",(" +
                    StandardValue($"{value}[{Quote(field.Value.Key)}]", field.Value.Type, context) + ")");
                return $"(& {{ {value} = {expression}; return ,([object[]]@(" + string.Join("; ", entries) + ")) })";
            }

            var assignments = fields.Select((field, index) =>
            {
                var source = layout == StandardFieldLayout.Positional ? index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : Quote(result ? field.Key : field.Value.Key);
                var target = Quote(result ? field.Value.Key : field.Key);
                return target + " = " + StandardValue($"{value}[{source}]", field.Value.Type, context);
            });
            return $"(& {{ {value} = {expression}; return @{{" + string.Join("; ", assignments) + "} })";
        }
    }
}
