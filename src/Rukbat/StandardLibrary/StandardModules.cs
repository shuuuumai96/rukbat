namespace Rukbat;

internal static class StandardModules
{
    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.Ordinal)
    {
        "fs", "path", "json", "csv", "env", "process", "text", "test", "encoding", "math", "datetime", "array", "zip", "hash", "http", "cli", "log", "re"
    };

    public static string Source(string name) => name switch
    {
        "cli" => CliDefinitions.Structures,
        "log" => LogValues.Structures,
        "re" => "struct Match { value: string; start: int; count: int; groups: dict[string, string?] }",
        "http" => HttpValues.Structures,
        "process" => "struct Result { exit_code: int; stdout: string; stderr: string }; " +
            "struct EnvironmentVariable { name: string; value: string? }; " +
            "struct Input { text: string; encoding: encoding.Encoding }; " +
            "struct Options { timeout_ms: int?; output_encoding: encoding.Encoding; working_dir: string?; environment: EnvironmentVariable[]; stdin: Input? }",
        "zip" => "struct Options { name_encoding: encoding.Encoding; max_entries: int; max_total_bytes: int }; " +
            "struct Entry { name: string; is_directory: bool; size: int; compressed_size: int }",
        "json" => "struct Value {}",
        "csv" => "struct Row {}; struct Table { columns: string[]; rows: Row[] }",
        "test" => "struct Context { temp_dir: string; source_dir: string }",
        "fs" => "struct Info { is_dir: bool; size: int?; modified: datetime.Timestamp }",
        _ => ""
    };

    public static string? Dependency(string name) => name switch
    {
        "fs" => "datetime",
        "process" or "zip" or "http" => "encoding",
        _ => null
    };
}
