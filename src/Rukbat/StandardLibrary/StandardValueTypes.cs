namespace Rukbat
{
    internal static class StandardValueTypes
    {
        // Scalar identities are shared by binding and boundary checks. Struct-backed
        // standard values must still come from the current compilation's modules.
        private static readonly IReadOnlyDictionary<string, ValueType> scalars = new[]
        {
            ValueType.Encoding, ValueType.RegexPattern, ValueType.FileLines,
            HashValues.Algorithm, LogValues.Level, HttpValues.Method, HttpValues.Body,
            DateTimeValues.Timestamp, DateTimeValues.Date, DateTimeValues.Duration, DateTimeValues.Zone
        }.ToDictionary(type => type.Name, StringComparer.Ordinal);

        public static ValueType? FindScalar(string moduleName, string name) =>
            scalars.GetValueOrDefault(moduleName + "." + name);

        public static bool IsTagged(ValueType type) =>
            type == ValueType.Encoding || type == HashValues.Algorithm || type == LogValues.Level;

        public static bool IsOpaqueScalar(ValueType type) =>
            scalars.TryGetValue(type.Name, out var scalar) && scalar == type;

        // Struct-backed opaque values retain the identities of their canonical standard modules.
        public static bool IsOpaque(ValueType type) =>
            IsOpaqueScalar(type) || type.IsCsvRow || type.IsJsonValue || type.IsCliResult;
    }
}
