using System.Text.RegularExpressions;

namespace Rukbat
{
    internal static class EditorCatalog
    {
        public static EditorSymbol Constant(StandardConstant constant) => constant.Definition switch
        {
            DateTimeValues.ZoneConstant zone => ZoneConstant(zone),
            StandardEncoding encoding => EncodingValue(encoding),
            LogValues.LevelConstant level => LogValue(level),
            HttpConstant http => HttpConstant(http),
            string algorithm => HashValue(algorithm),
            _ => throw new InvalidOperationException($"Unsupported standard constant '{constant.Name}'.")
        };

        public static readonly EditorSymbol ArrayLength = new(ContainerMember.Length, "length: int", CompletionKind.Field, "The number of array elements.");
        public static readonly EditorSymbol DictionaryLength = new(ContainerMember.Length, "length: int", CompletionKind.Field, "The number of dictionary entries. Read-only.");

        public static EditorSymbol ArrayAppend(ValueType element) =>
            new(ContainerMember.Append, $"append(value: {element}): void", CompletionKind.Function);

        public static IEnumerable<EditorSymbol> DictionaryMembers(ValueType type) =>
        [
            DictionaryLength,
            new(ContainerMember.Has, $"has(key: {type.DictionaryKey}): bool", CompletionKind.Function, "Test whether a key exists, including entries storing null."),
            new(ContainerMember.Remove, $"remove(key: {type.DictionaryKey}): void", CompletionKind.Function, "Remove a key from a writable dictionary. An absent key succeeds."),
            new(ContainerMember.Keys, $"keys(): {type.DictionaryKey}[]", CompletionKind.Function, "Return an independent snapshot of keys in insertion order.")
        ];
        public static EditorSymbol HttpConstant(HttpConstant value) => new(value.Name, $"http.{value.Name}: {value.Type} = {value.Value}", CompletionKind.Constant,
            "Read-only HTTP constant.", Type: value.Type);
        public static EditorSymbol HttpType(ValueType value) => new(value.Name.Split('.')[1], value.Name, CompletionKind.Enum,
            value == HttpValues.Body ? "Immutable HTTP response bytes. Decode with http.text and an explicit encoding." : "A validated HTTP method. Use http.method_* constants.", Type: value);
        // Embed the canonical API documentation rather than maintain a second list of signatures.
        public static readonly EditorSymbol HashType = new("Algorithm", "hash.Algorithm", CompletionKind.Enum,
            "A validated checksum algorithm. Use hash.parse/name at external boundaries.", Type: HashValues.Algorithm);
        public static EditorSymbol HashValue(string name) => new(name, $"hash.{name}: hash.Algorithm", CompletionKind.EnumMember, HashValues.Description(name), Type: HashValues.Algorithm);
        public static readonly EditorSymbol LogType = new("Level", "log.Level", CompletionKind.Enum,
            "A validated logging severity. Use log.level_debug/info/warn/error constants.", Type: LogValues.Level);
        public static EditorSymbol LogValue(LogValues.LevelConstant value) => new(value.Name,
            $"log.{value.Name}: log.Level", CompletionKind.Constant, value.Documentation, Type: LogValues.Level);
        public static readonly EditorSymbol EncodingType = new("Encoding", "encoding.Encoding", CompletionKind.Enum,
            "A validated encoding type. Use predefined encoding values or encoding.from_code_page for an explicit Windows code page.", Type: ValueType.Encoding);
        public static readonly EditorSymbol FileLinesType = new("Lines", "fs.Lines", CompletionKind.Enum,
            "An immutable file-line descriptor. Each for opens an independent traversal and closes it on termination.", Type: ValueType.FileLines);
        public static readonly EditorSymbol CsvRecordsType = new("Records", "csv.Records", CompletionKind.Enum,
            "An immutable CSV file descriptor. Each for opens an independent traversal of CSV records.", Type: ValueType.CsvRecords);
        public static readonly EditorSymbol RegexPatternType = new("Pattern", "re.Pattern", CompletionKind.Enum,
            "An immutable validated regular expression with an explicit finite match timeout. Create with re.compile.", Type: ValueType.RegexPattern);
        public static EditorSymbol EncodingValue(StandardEncoding value) => new(value.Name,
            $"encoding.{value.Name}: encoding.Encoding", CompletionKind.EnumMember, value.Documentation + " Read-only.", Type: ValueType.Encoding);
        public static EditorSymbol DateTimeType(ValueType value) => new(value.Name.Split('.')[1], value.Name, CompletionKind.Enum, DateTimeValues.Description(value), Type: value);
        private static readonly EditorSymbol[] scalarTypes =
        [
            HashType, LogType, HttpType(HttpValues.Method), HttpType(HttpValues.Body),
            EncodingType, FileLinesType, CsvRecordsType, RegexPatternType,
            new("Rows", "csv.Rows", CompletionKind.Enum, "CSV traversal with named columns.", Type: ValueType.CsvRows),
            new("FileWalk", "fs.FileWalk", CompletionKind.Enum, "Sequential recursive file traversal.", Type: ValueType.FileWalk),
            new("LockScope", "fs.LockScope", CompletionKind.Enum, "Loop-owned exclusive file lock.", Type: ValueType.FileLock),
            new("TextStage", "fs.TextStage", CompletionKind.Enum, "Loop-owned staged file output.", Type: ValueType.TextStage),
            new("TextWriter", "fs.TextWriter", CompletionKind.Enum, "Writer valid within its staging loop.", Type: ValueType.TextWriter),
            .. DateTimeValues.All.Select(DateTimeType)
        ];
        private static readonly IReadOnlyDictionary<ValueType, EditorSymbol> scalarSymbols =
            scalarTypes.ToDictionary(symbol => symbol.Type!);
        private static readonly ILookup<string, EditorSymbol> moduleTypes =
            scalarTypes.ToLookup(symbol => symbol.Type!.Name.Split('.')[0], StringComparer.Ordinal);

        public static EditorSymbol? ScalarType(ValueType type) => scalarSymbols.GetValueOrDefault(type);
        public static IEnumerable<EditorSymbol> TypesForModule(string moduleName) => moduleTypes[moduleName];

        public static EditorSymbol ZoneConstant(DateTimeValues.ZoneConstant value) => new(value.Name,
            $"datetime.{value.Name}: datetime.Zone", CompletionKind.Constant, value.Documentation, Type: DateTimeValues.Zone);
        public static readonly List<EditorSymbol> Standard = Load();
        public static readonly string[] Modules = Standard.Select(s => s.Detail.Split('.')[0]).Distinct().Order().ToArray();
        public static readonly string[] Keywords = ["import",
            "fn",
            "struct",
            "enum",
            "impl",
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
            new("print", "print(value): void", CompletionKind.Function, "Writes one value and a newline to standard output."),
            new("eprint", "eprint(value): void", CompletionKind.Function, "Writes one value and a newline to standard error."),
            new("args", "args(): string[]", CompletionKind.Function, "Returns the entry program's arguments."),
            new("exit", "exit(code: int): void", CompletionKind.Function, "Terminates the program with a nonnegative exit code."),
            new("powershell", "powershell<T>(command: string, ...): T", CompletionKind.Function, "Explicit PowerShell escape hatch; validates the returned value against T."),
            new("Error", "struct Error { message: string }", CompletionKind.Struct, "The ordinary runtime error value.")
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
                {
                    continue;
                }

                var docs = match.Groups["docs"].Value.Trim().TrimEnd('|').Trim().Replace(" | ", "\n\n");
                result.Add(new(match.Groups["name"].Value, match.Groups["signature"].Value, CompletionKind.Function, docs));
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
}
