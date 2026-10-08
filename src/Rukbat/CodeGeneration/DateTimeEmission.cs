namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitDateTimeOperations()
    {
        foreach (var operation in DateTimeValues.Operations)
            Line($"'datetime.{operation.Name}' {{ return {operation.Expression} }}");
    }

    private static string Comparison(string left, string right, string operation, ValueType type) =>
        DateTimeValues.Ordered(type)
            ? $"([RukbatDateTime]::Compare(({left}), ({right})) {operation} 0)"
            : $"({left} {operation} {right})";
}
