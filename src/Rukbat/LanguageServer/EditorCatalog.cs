using System.Text.RegularExpressions;

namespace Rukbat;

internal static class EditorCatalog
{
    public static IEnumerable<EditorSymbol> DictionaryMembers(ValueType type) =>
    [
        new("length", "length: int", 5, "The number of dictionary entries. Read-only."),
        new("has", $"has(key: {type.DictionaryKey}): bool", 3, "Test whether a key exists, including entries storing null."),
        new("remove", $"remove(key: {type.DictionaryKey}): void", 3, "Remove a key from a writable dictionary. An absent key succeeds."),
        new("keys", $"keys(): {type.DictionaryKey}[]", 3, "Return an independent snapshot of keys in insertion order.")
    ];
    public static EditorSymbol HttpConstant(HttpConstant value) => new(value.Name, $"http.{value.Name}: {value.Type} = {value.Value}", 21,
        "Read-only HTTP constant.", Type: value.Type);
    public static EditorSymbol HttpType(ValueType value) => new(value.Name.Split('.')[1], value.Name, 13,
        value == HttpValues.Body ? "Immutable HTTP response bytes. Decode with http.text and an explicit encoding." : "A validated HTTP method. Use http.method_* constants.", Type: value);
    // Embed the canonical API documentation rather than maintain a second list of signatures.
    public static readonly EditorSymbol HashType = new("Algorithm", "hash.Algorithm", 13,
        "A validated checksum algorithm. Use hash.parse/name at external boundaries.", Type: HashValues.Algorithm);
    public static EditorSymbol HashValue(string name) => new(name, $"hash.{name}: hash.Algorithm", 20, HashValues.Description(name), Type: HashValues.Algorithm);
    public static readonly EditorSymbol LogType = new("Level", "log.Level", 13,
        "A validated logging severity. Use log.level_debug/info/warn/error constants.", Type: LogValues.Level);
    public static EditorSymbol LogValue(LogValues.LevelConstant value) => new(value.Name,
        $"log.{value.Name}: log.Level", 21, value.Documentation, Type: LogValues.Level);
    public static readonly EditorSymbol EncodingType = new("Encoding", "encoding.Encoding", 13,
        "A validated encoding type. Use predefined encoding values or encoding.from_code_page for an explicit Windows code page.", Type: ValueType.Encoding);
    public static readonly EditorSymbol FileLinesType = new("Lines", "fs.Lines", 13,
        "An immutable file-line descriptor. Each for opens an independent traversal and closes it on termination.", Type: ValueType.FileLines);
    public static EditorSymbol EncodingValue(StandardEncoding value) => new(value.Name,
        $"encoding.{value.Name}: encoding.Encoding", 20, value.Documentation + " Read-only.", Type: ValueType.Encoding);
    public static EditorSymbol DateTimeType(ValueType value) => new(value.Name.Split('.')[1], value.Name, 13, DateTimeValues.Description(value), Type: value);
    public static EditorSymbol ZoneConstant(DateTimeValues.ZoneConstant value) => new(value.Name,
        $"datetime.{value.Name}: datetime.Zone", 21, value.Documentation, Type: DateTimeValues.Zone);
    public static readonly List<EditorSymbol> Standard = Load();
    public static readonly string[] Modules = Standard.Select(s => s.Detail.Split('.')[0]).Distinct().Order().ToArray();
    public static readonly string[] Keywords = ["import",
        "fn",
        "struct",
        "return",
        "if",
        "else",
        "for",
        "in",
        "while",
        "break",
        "continue",
        "try",
        "catch",
        "throw",
        "true",
        "false",
        "null",
        "int",
        "float",
        "string",
        "bool",
        "void"];
    public static readonly EditorSymbol[] Builtins =
    [
        new("print", "print(value): void", 3, "Writes one value and a newline to standard output."),
        new("eprint", "eprint(value): void", 3, "Writes one value and a newline to standard error."),
        new("args", "args(): string[]", 3, "Returns the entry program's arguments."),
        new("exit", "exit(code: int): void", 3, "Terminates the program with a nonnegative exit code."),
        new("powershell", "powershell<T>(command: string, ...): T", 3, "Explicit PowerShell escape hatch; validates the returned value against T."),
        new("Error", "struct Error { message: string }", 22, "The ordinary runtime error value.")
    ];
    public static readonly IReadOnlyDictionary<string, EditorCallable> Signatures = Standard.Concat(Builtins)
        .Select(EditorCallable.From).OfType<EditorCallable>().DistinctBy(s => s.Label).ToDictionary(s => s.Label, StringComparer.Ordinal);
    private static List<EditorSymbol> Load()
    {
        using var stream = typeof(EditorCatalog).Assembly.GetManifestResourceStream("Rukbat.StandardLibrary.md")!;
        using var reader = new StreamReader(stream);
        var result = new List<EditorSymbol>();
        foreach (var line in reader.ReadToEnd().Split('\n'))
        {
            var match = Regex.Match(line, @"^\| `(?<signature>(?<module>[a-z]+)\.(?<name>[a-z_]+)(?:<T>)?\([^`]+)` \| (?<docs>.*)");
            if (!match.Success)
                continue;
            var docs = match.Groups["docs"].Value.Trim().TrimEnd('|').Trim().Replace(" | ", "\n\n");
            result.Add(new(match.Groups["name"].Value, match.Groups["signature"].Value, 3, docs));
        }
        // Zero-argument signatures are covered by the same expression (the ')' is nonempty).
        result.AddRange(HashValues.All.Select(HashValue));
        result.AddRange(LogValues.Levels.Select(LogValue));
        result.AddRange(HttpValues.Constants.Select(HttpConstant));
        result.AddRange(EncodingValues.All.Select(EncodingValue));
        result.AddRange(DateTimeValues.Zones.Select(ZoneConstant));
        return result;
    }
}
