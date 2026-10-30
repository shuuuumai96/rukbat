namespace Rukbat
{
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
}
