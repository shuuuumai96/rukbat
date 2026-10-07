namespace Rukbat;

internal static class CliDefinitions
{
    // The loader and ordinary struct binding own all public definition fields.
    public const string Structures = """
        struct Command {
            name: string; description: string
            arguments: Argument[]; options: Option[]; flags: Flag[]
        }
        struct Argument {
            name: string; description: string
            required: bool; repeatable: bool; allow_empty: bool; choices: string[]
        }
        struct Option {
            name: string; description: string; short_name: string?; value_name: string
            required: bool; default_value: string?; repeatable: bool; allow_empty: bool; choices: string[]
        }
        struct Flag { name: string; description: string; short_name: string? }
        struct Result {}
        """;
}

internal sealed partial class TypeChecker
{
    private ValueType CliCallType(Call call, Symbols scope, Module standard)
    {
        ValueType Structure(string name) => standard.Structs[name].BoundType;
        ValueType[]? parameters = call.Name switch
        {
            "command" or "argument" or "option" or "flag" => [ValueType.String, ValueType.String],
            "parse" => [Structure("Command"), ValueType.ArrayOf(ValueType.String)],
            "help" => [Structure("Command")],
            "help_requested" => [Structure("Result")],
            "has" or "get" or "get_all" => [Structure("Result"), ValueType.String],
            _ => null
        };
        if (parameters is null)
        {
            Error("RUK2004", $"Unknown standard-library function 'cli.{call.Name}'.", call);
            return ValueType.Error;
        }
        if (call.TypeArgument is not null)
            Error("RUK2005", $"cli.{call.Name} does not accept a type argument.", call);
        CheckStandardArguments(call, scope, parameters, $"cli.{call.Name}");
        StandardCalls[call] = "cli." + call.Name;
        return call.Name switch
        {
            "command" => Structure("Command"),
            "argument" => Structure("Argument"),
            "option" => Structure("Option"),
            "flag" => Structure("Flag"),
            "parse" => Structure("Result"),
            "help_requested" or "has" => ValueType.Bool,
            "get_all" => ValueType.ArrayOf(ValueType.String),
            _ => ValueType.String
        };
    }
}
