namespace Rukbat;

internal sealed record VariableSymbol(string Name, ValueType Type, string PowerShellName, bool IsGlobal = false, bool IsConst = false);
internal sealed record FunctionSymbol(Module Module, Function Function, string PowerShellName, StructSymbol? Owner = null)
{
    public ValueType ReturnType { get; set; } = ValueType.Error;
}
internal sealed class Symbols(Symbols? parent = null)
{
    public Dictionary<string, VariableSymbol> Variables { get; } = new(StringComparer.Ordinal);
    public VariableSymbol? Find(string name) => Variables.GetValueOrDefault(name) ?? parent?.Find(name);
}
