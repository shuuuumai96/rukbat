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

        private static string Comparison(string left, string right, string operation, ValueType type, ValueType rightType)
        {
            if (operation is "-ceq" or "-cne" && (type == ValueType.Null || rightType == ValueType.Null))
            {
                // PowerShell filters arrays on the left of equality. Null comparisons
                // must produce one boolean, including in assertions and for empty arrays.
                var value = type == ValueType.Null ? right : left;
                return $"($null {operation} ({value}))";
            }

            if (type == ValueType.String && operation is "-ceq" or "-cne")
            {
                var equality = $"[string]::Equals(({left}), ({right}), [System.StringComparison]::Ordinal)";
                return operation == "-ceq" ? $"({equality})" : $"(-not {equality})";
            }

            return DateTimeValues.Ordered(type)
                ? $"([RukbatDateTime]::Compare(({left}), ({right})) {operation} 0)"
                : $"({left} {operation} {right})";
        }
    }
}
