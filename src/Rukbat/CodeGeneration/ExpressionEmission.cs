using System.Globalization;

namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private string Expr(Expression expression)
        {
            // Ordinary expressions copy values; freshly allocated values are already independent.
            var value = RawExpr(expression);
            if (IsFreshValue(expression))
            {
                return value;
            }
            var actual = checker.Types[expression];
            return Clone(value, actual);
        }

        private string ReadExpr(Expression expression) => RawExpr(expression);

        private static string Clone(string value, ValueType type) => NeedsClone(type)
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

        private delegate string ExpressionStrategy(Emitter emitter, Expression expression);

        private static KeyValuePair<Type, ExpressionStrategy> ExpressionHandler<TExpression>(Func<Emitter, TExpression, string> emit)
            where TExpression : Expression =>
            new(typeof(TExpression), (emitter, expression) => emit(emitter, (TExpression)expression));

        private static readonly IReadOnlyDictionary<Type, ExpressionStrategy> AddressEmitters = new[]
        {
            ExpressionHandler<Name>(static (emitter, name) => emitter.VariableReference(name)),
            ExpressionHandler<Member>(static (emitter, member) => emitter.MemberAddress(member)),
            ExpressionHandler<Index>(static (emitter, index) => emitter.IndexAddress(index))
        }.ToDictionary(handler => handler.Key, handler => handler.Value);

        private string Address(Expression expression) => AddressEmitters[expression.GetType()](this, expression);

        private string VariableReference(Name name) => checker.Variables[name].PowerShellName;

        private string MemberAddress(Member member)
        {
            var receiver = "$__rukbat_receiver" + temporaryId++;
            Line($"{receiver} = {RawExpr(member.Receiver)}");
            return $"{receiver}[{Quote(checker.Members[member].Field!.Key)}]";
        }

        private static readonly IReadOnlyDictionary<Type, ExpressionStrategy> ExpressionEmitters = new[]
        {
            ExpressionHandler<Literal>(static (_, literal) => EmitLiteral(literal)),
            ExpressionHandler<ArrayLiteral>(static (emitter, array) => emitter.EmitArrayLiteral(array)),
            ExpressionHandler<DictionaryLiteral>(static (emitter, dictionary) => emitter.DictionaryLiteral(dictionary)),
            ExpressionHandler<Index>(static (emitter, index) => emitter.EmitIndex(index)),
            ExpressionHandler<Name>(static (emitter, name) => emitter.VariableReference(name)),
            ExpressionHandler<Member>(static (emitter, member) => emitter.EmitMember(member)),
            ExpressionHandler<Unary>(static (emitter, unary) => emitter.EmitUnary(unary)),
            ExpressionHandler<Binary>(static (emitter, binary) => emitter.Binary(binary)),
            ExpressionHandler<Call>(static (emitter, call) => emitter.Call(call))
        }.ToDictionary(handler => handler.Key, handler => handler.Value);

        private string RawExpr(Expression expression) => ExpressionEmitters[expression.GetType()](this, expression);

        private static string EmitLiteral(Literal literal) => literal.Value switch
        {
            string value => Quote(value),
            bool value => value ? "$true" : "$false",
            long value => $"([long]{Quote(value.ToString(CultureInfo.InvariantCulture))})",
            double value => $"([double]{value.ToString("R", CultureInfo.InvariantCulture)})",
            NullValue => "$null",
            _ => throw new InvalidOperationException("Unsupported literal.")
        };

        private static bool NeedsArrayElementWrap(ValueType type) =>
            type.NonNullable.Kind == ValueKind.Array;

        private string EmitArrayLiteral(ArrayLiteral array) => array.Items.Count == 0
            ? "([System.Collections.Generic.List[object]]::new())"
            : $"(& $script:__rukbat_array @({string.Join("; ", array.Items.Select(item => NeedsArrayElementWrap(checker.Types[item]) ? ",(" + Expr(item) + ")" : "(" + Expr(item) + ")"))}))";

        private string EmitIndex(Index index) => checker.Types[index.Receiver].Kind == ValueKind.Dictionary
            ? $"(& $script:__rukbat_dictionary 'get' ({RawExpr(index.Receiver)}) ({Expr(index.Offset)}) $null {Location(index)} $script:__rukbat_raise)"
            : $"(& $script:__rukbat_index ({RawExpr(index.Receiver)}) ({Expr(index.Offset)}) {Location(index)} $false $script:__rukbat_raise)";

        private static readonly IReadOnlyDictionary<(string Operator, bool IsFloat), Func<string, string>> UnaryStrategies =
            new Dictionary<(string, bool), Func<string, string>>
            {
                [(RuntimeCatalog.Operators.Not, false)] = static operand => $"(-not {operand})",
                [(RuntimeCatalog.Operators.Add, true)] = static operand => $"([double](+{operand}))",
                [(RuntimeCatalog.Operators.Subtract, true)] = static operand => $"([double](-{operand}))",
                [(RuntimeCatalog.Operators.Add, false)] = static operand => $"({operand})",
                [(RuntimeCatalog.Operators.Subtract, false)] = static operand => $"([Convert]::ToInt64(-[decimal]({operand})))"
            };

        private string EmitUnary(Unary unary)
        {
            var operand = Expr(unary.Value);
            return UnaryStrategies[(unary.Token.Text, checker.Types[unary] == ValueType.Float)](operand);
        }

        private static readonly IReadOnlyDictionary<MemberKind, Func<Emitter, Member, BoundMember, string>> MemberEmitters =
            new Dictionary<MemberKind, Func<Emitter, Member, BoundMember, string>>
            {
                [MemberKind.EnumMember] = static (_, _, binding) => Quote(binding.EnumMember!.Token.Text),
                [MemberKind.StandardConstant] = static (_, _, binding) => Constant(binding.Constant!),
                [MemberKind.ArrayLength] = static (emitter, member, _) => $"([long]({emitter.RawExpr(member.Receiver)}).Count)",
                [MemberKind.DictionaryLength] = static (emitter, member, _) => $"([long]({emitter.RawExpr(member.Receiver)})['entries'].Count)",
                [MemberKind.Field] = static (emitter, member, binding) => $"(({emitter.RawExpr(member.Receiver)})[{Quote(binding.Field!.Key)}])"
            };

        private string EmitMember(Member member)
        {
            var binding = checker.Members[member];
            return MemberEmitters[binding.Kind](this, member, binding);
        }

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

        private delegate string BinaryStrategy(Emitter emitter, Binary binary, string left, string right);

        private static readonly IReadOnlyDictionary<string, Func<string, string, string>> IntegerBinaryStrategies =
            new Dictionary<string, Func<string, string, string>>(StringComparer.Ordinal)
            {
                [RuntimeCatalog.Operators.Add] = static (left, right) => $"([Convert]::ToInt64([decimal]({left}) + [decimal]({right})))",
                [RuntimeCatalog.Operators.Subtract] = static (left, right) => $"([Convert]::ToInt64([decimal]({left}) - [decimal]({right})))",
                [RuntimeCatalog.Operators.Multiply] = static (left, right) => $"([Convert]::ToInt64([decimal]({left}) * [decimal]({right})))",
                [RuntimeCatalog.Operators.Divide] = static (left, right) => $"(& $script:__rukbat_int_div ({left}) ({right}) $false)",
                [RuntimeCatalog.Operators.Modulo] = static (left, right) => $"(& $script:__rukbat_int_div ({left}) ({right}) $true)"
            };

        private static readonly IReadOnlyDictionary<ValueType, BinaryStrategy> BinaryEmitters =
            new Dictionary<ValueType, BinaryStrategy>
            {
                [ValueType.Int] = static (_, binary, left, right) => IntegerBinaryStrategies[binary.Token.Text](left, right),
                [ValueType.Float] = static (emitter, binary, left, right) =>
                    $"(& $script:__rukbat_float ([double]({emitter.EmitComparison(binary, left, right)})) {emitter.Location(binary)} $script:__rukbat_raise)"
            };

        private string Binary(Binary binary)
        {
            var strategy = BinaryEmitters.GetValueOrDefault(checker.Types[binary], EmitOrdinaryBinary);
            var readOnly = RuntimeCatalog.EqualityOperators.Contains(binary.Token.Text);
            var left = readOnly ? ReadExpr(binary.Left) : Expr(binary.Left);
            var right = readOnly ? ReadExpr(binary.Right) : Expr(binary.Right);
            return strategy(this, binary, left, right);
        }

        private static string EmitOrdinaryBinary(Emitter emitter, Binary binary, string left, string right) =>
            emitter.EmitComparison(binary, left, right);

        private string EmitComparison(Binary binary, string left, string right) =>
            Comparison(left, right, BinaryOperator(binary.Token.Text), checker.Types[binary.Left], checker.Types[binary.Right]);

        private static string BinaryOperator(string operation) =>
            BinaryOperators.GetValueOrDefault(operation, operation);

        private static readonly IReadOnlyDictionary<string, string> BinaryOperators =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RuntimeCatalog.Operators.Equal] = "-ceq",
                [RuntimeCatalog.Operators.NotEqual] = "-cne",
                [RuntimeCatalog.Operators.Less] = "-lt",
                [RuntimeCatalog.Operators.LessOrEqual] = "-le",
                [RuntimeCatalog.Operators.Greater] = "-gt",
                [RuntimeCatalog.Operators.GreaterOrEqual] = "-ge",
                [RuntimeCatalog.Operators.And] = "-and",
                [RuntimeCatalog.Operators.Or] = "-or"
            };
    }
}
