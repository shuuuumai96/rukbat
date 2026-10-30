namespace Rukbat
{
    internal static class StandardValueTypes
    {
        public static bool IsTagged(ValueType type) =>
            type == ValueType.Encoding || type == HashValues.Algorithm || type == LogValues.Level;

        public static bool IsOpaqueScalar(ValueType type) =>
            IsTagged(type) || type == ValueType.RegexPattern || type == ValueType.FileLines ||
            DateTimeValues.Contains(type) || HttpValues.Contains(type);

        // Struct-backed opaque values retain the identities of their canonical standard modules.
        public static bool IsOpaque(ValueType type) =>
            IsOpaqueScalar(type) || type.IsCsvRow || type.IsJsonValue || type.IsCliResult;
    }
}
