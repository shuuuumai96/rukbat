namespace Rukbat
{
    internal enum CallKind
    {
        Unbound,
        DictionaryMethod,
        ArrayAppend,
        Standard,
        StructLiteral,
        PowerShell,
        Arguments,
        Print,
        PrintError,
        Exit,
        LocalFunction,
        ImportedFunction
    }

    internal readonly record struct BoundCall(CallKind Kind, FunctionSymbol? Function = null,
        StructSymbol? Structure = null, string? StandardOperation = null);
}
