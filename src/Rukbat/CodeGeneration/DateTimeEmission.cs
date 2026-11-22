namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private void EmitDateTimeOperations()
        {
            foreach (var operation in DateTimeValues.Operations)
            {
                Line($"'datetime.{operation.Name}' {{ return {operation.Expression} }}");
            }
        }

        private enum ComparisonKind { Ordinary, Null, OrdinalText, DateTime }

        private delegate string ComparisonStrategy(string left, string right, string operation, ValueType leftType);

        private static readonly IReadOnlyDictionary<ComparisonKind, ComparisonStrategy> ComparisonEmitters =
            new Dictionary<ComparisonKind, ComparisonStrategy>
            {
                [ComparisonKind.Ordinary] = static (left, right, operation, _) => $"({left} {operation} {right})",
                [ComparisonKind.Null] = static (left, right, operation, leftType) =>
                    $"($null {operation} ({(leftType == ValueType.Null ? right : left)}))",
                [ComparisonKind.OrdinalText] = static (left, right, operation, _) => EmitOrdinalComparison(left, right, operation),
                [ComparisonKind.DateTime] = static (left, right, operation, _) =>
                    $"([RukbatDateTime]::Compare(({left}), ({right})) {operation} 0)"
            };

        private static string Comparison(string left, string right, string operation, ValueType type, ValueType rightType)
        {
            // PowerShell filters arrays on the left of equality. Null comparisons must produce one boolean.
            var kind = operation switch
            {
                "-ceq" or "-cne" when type == ValueType.Null || rightType == ValueType.Null => ComparisonKind.Null,
                "-ceq" or "-cne" when type == ValueType.String || type.Kind == ValueKind.Enum => ComparisonKind.OrdinalText,
                _ when DateTimeValues.Ordered(type) => ComparisonKind.DateTime,
                _ => ComparisonKind.Ordinary
            };
            return ComparisonEmitters[kind](left, right, operation, type);
        }

        private static string EmitOrdinalComparison(string left, string right, string operation)
        {
            var equality = $"[string]::Equals(({left}), ({right}), [System.StringComparison]::Ordinal)";
            return operation == "-ceq" ? $"({equality})" : $"(-not {equality})";
        }
    }
}
