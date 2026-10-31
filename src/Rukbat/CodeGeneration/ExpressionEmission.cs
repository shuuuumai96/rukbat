using System.Globalization;

namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private string Expr(Expression expression)
        {
            // Ordinary expressions copy values; assignment targets and self use RawExpr or Address.
            var value = RawExpr(expression);
            var actual = checker.Types[expression];
            value = Clone(value, actual);
            return value;
        }

        private static string Clone(string value, ValueType type) => type.IsComposite
            ? $"(& $script:__rukbat_clone ({value}) $script:__rukbat_clone)"
            : value;

        private string Location(Node node)
        {
            var line = module.Source.Text[..node.Token.Offset].Count(character => character == '\n') + 1;
            return Quote(SourceName(module) + ":" + line);
        }

        private string IndexAddress(Index index)
        {
            // Evaluate a potentially side-effecting index only once and retain its target in a temporary.
            var slot = "$__rukbat_slot" + temporaryId++;
            if (checker.Types[index.Receiver].DictionaryKey is not null)
            {
                Line($"{slot} = & $script:__rukbat_dictionary 'entry' ({RawExpr(index.Receiver)}) ({Expr(index.Offset)}) $null {Location(index)} $script:__rukbat_raise");
                return $"{slot}['value']";
            }
            Line($"{slot} = & $script:__rukbat_index ({RawExpr(index.Receiver)}) ({Expr(index.Offset)}) {Location(index)} $true $script:__rukbat_raise");
            return $"{slot}.array[{slot}.offset]";
        }

        private string Address(Expression expression) => expression switch
        {
            Name name => checker.Variables[name].PowerShellName,
            Member member => $"{Address(member.Receiver)}[{Quote(checker.Fields[member].Key)}]",
            Index index => IndexAddress(index),
            _ => throw new InvalidOperationException("Invalid field assignment target.")
        };

        private string RawExpr(Expression expression) => expression switch
        {
            Literal literal => literal.Value switch
            {
                string value => Quote(value),
                bool value => value ? "$true" : "$false",
                long value => $"([long]{Quote(value.ToString(CultureInfo.InvariantCulture))})",
                double value => $"([double]{value.ToString("R", CultureInfo.InvariantCulture)})",
                NullValue => "$null",
                _ => throw new InvalidOperationException()
            },
            ArrayLiteral array => $"(& $script:__rukbat_array @({string.Join("; ", array.Items.Select(item => ",(" + Expr(item) + ")"))}))",
            DictionaryLiteral dictionary => DictionaryLiteral(dictionary),
            Index index when checker.Types[index.Receiver].DictionaryKey is not null => $"(& $script:__rukbat_dictionary 'get' ({RawExpr(index.Receiver)}) ({Expr(index.Offset)}) $null {Location(index)} $script:__rukbat_raise)",
            Index index => $"(& $script:__rukbat_index ({RawExpr(index.Receiver)}) ({Expr(index.Offset)}) {Location(index)} $false $script:__rukbat_raise)",
            Name name => checker.Variables[name].PowerShellName,
            Member member when checker.Constants.TryGetValue(member, out var constant) => Constant(constant),
            Member member when checker.Types[member.Receiver].Element is not null => $"([long]({RawExpr(member.Receiver)}).Count)",
            Member member when checker.Types[member.Receiver].DictionaryKey is not null => $"([long]({RawExpr(member.Receiver)})['entries'].Count)",
            Member member => $"(({RawExpr(member.Receiver)})[{Quote(checker.Fields[member].Key)}])",
            Unary unary => unary.Token.Text == "!"
                ? $"(-not {Expr(unary.Value)})"
                : checker.Types[unary] == ValueType.Float
                    ? $"([double]({unary.Token.Text}{Expr(unary.Value)}))"
                    : unary.Token.Text == "+" ? $"({Expr(unary.Value)})" : $"([RukbatNumbers]::Integer('negate', ({Expr(unary.Value)}), 0))",
            Binary binary => Binary(binary),
            Call call => Call(call),
            _ => throw new InvalidOperationException("Unbound expression.")
        };

        private static string Constant(StandardConstant constant) => constant.Definition switch
        {
            DateTimeValues.ZoneConstant zone => "(" + zone.Expression + ")",
            StandardEncoding encoding => Quote(encoding.Label),
            LogValues.LevelConstant level => Quote(level.Tag),
            HttpConstant { Value: string method } => Quote(method),
            HttpConstant { Value: long status } => "[long]" + status.ToString(CultureInfo.InvariantCulture),
            string algorithm => Quote(algorithm),
            _ => throw new InvalidOperationException($"Unsupported standard constant '{constant.Name}'.")
        };

        private string Binary(Binary binary)
        {
            if (binary.Token.Text is "==" or "!=" &&
                (binary.Left is Literal { Value: NullValue } || binary.Right is Literal { Value: NullValue }))
            {
                var value = binary.Left is Literal { Value: NullValue } ? binary.Right : binary.Left;
                var nullOperator = binary.Token.Text == "==" ? "-ceq" : "-cne";
                // PowerShell filters when an array is on the left of -eq/-ne. Keep null on the left
                // so nullable array comparison always produces one boolean, including for [].
                return $"($null {nullOperator} {Expr(value)})";
            }

            var op = BinaryOperator(binary.Token.Text);
            var expression = Comparison(Expr(binary.Left), Expr(binary.Right), op, checker.Types[binary.Left]);
            if (checker.Types[binary] == ValueType.Int)
            {
                return $"([RukbatNumbers]::Integer({Quote(binary.Token.Text)}, ({Expr(binary.Left)}), ({Expr(binary.Right)})))";
            }
            if (checker.Types[binary] == ValueType.Float)
            {
                return $"(& $script:__rukbat_float ([double]({expression})) {Location(binary)} $script:__rukbat_raise)";
            }

            return expression;
        }

        private static string BinaryOperator(string operation) => operation switch
        {
            "==" => "-ceq",
            "!=" => "-cne",
            "<" => "-lt",
            "<=" => "-le",
            ">" => "-gt",
            ">=" => "-ge",
            "&&" => "-and",
            "||" => "-or",
            _ => operation
        };
    }
}
