namespace Rukbat
{
    internal sealed record StandardSignature(ValueType[] Parameters, ValueType Result);

    // Context-dependent generic operations (array and JSON) are checked separately.
    // Bound struct types must come from this compilation's canonical standard module.
    internal static class StandardSignatures
    {
        private static readonly IReadOnlyDictionary<(string Module, string Operation), Func<Module, StandardSignature>> Signatures =
            new Dictionary<(string, string), Func<Module, StandardSignature>>
            {
                [("re", "compile")] = static _ => new([ValueType.String, ValueType.Int], ValueType.RegexPattern),
                [("re", "is_match")] = static _ => new([ValueType.String, ValueType.RegexPattern], ValueType.Bool),
                [("re", "find")] = static module => new([ValueType.String, ValueType.RegexPattern], ValueType.NullableOf(Structure(module, "Match"))),
                [("re", "find_all")] = static module => new([ValueType.String, ValueType.RegexPattern], ValueType.ArrayOf(Structure(module, "Match"))),
                [("re", "replace_all")] = static _ => new([ValueType.String, ValueType.RegexPattern, ValueType.String], ValueType.String),
                [("re", "replace_all_template")] = static _ => new([ValueType.String, ValueType.RegexPattern, ValueType.String], ValueType.String),
                [("re", "split")] = static _ => new([ValueType.String, ValueType.RegexPattern], ValueType.ArrayOf(ValueType.String)),
                [("re", "escape")] = static _ => new([ValueType.String], ValueType.String),
                [("fs", "stat")] = static module => new([ValueType.String], Structure(module, "Info")),
                [("fs", "list_files")] = static _ => new([ValueType.String, ValueType.String], ValueType.ArrayOf(ValueType.String)),
                [("fs", "list_files_recursive")] = static _ => new([ValueType.String, ValueType.String], ValueType.ArrayOf(ValueType.String)),
                [("fs", "list_dirs")] = static _ => new([ValueType.String, ValueType.String], ValueType.ArrayOf(ValueType.String)),
                [("fs", "read_text")] = static _ => new([ValueType.String, ValueType.Encoding], ValueType.String),
                [("fs", "walk_files")] = static _ => new([ValueType.String, ValueType.String], ValueType.FileWalk),
                [("fs", "try_lock")] = static _ => new([ValueType.String], ValueType.FileLock),
                [("fs", "stage_text")] = static _ => new([ValueType.String, ValueType.Encoding], ValueType.TextStage),
                [("fs", "write_chunk")] = static _ => new([ValueType.TextWriter, ValueType.String], ValueType.Void),
                [("fs", "commit")] = static _ => new([ValueType.TextWriter], ValueType.Void),
                [("fs", "lines")] = static _ => new([ValueType.String, ValueType.Encoding], ValueType.FileLines),
                [("fs", "is_file")] = static _ => new([ValueType.String], ValueType.Bool),
                [("fs", "remove_file")] = static _ => new([ValueType.String], ValueType.Void),
                [("fs", "write_text")] = static _ => new([ValueType.String, ValueType.String, ValueType.Encoding], ValueType.Void),
                [("fs", "append_text")] = static _ => new([ValueType.String, ValueType.String, ValueType.Encoding], ValueType.Void),
                [("fs", "create_temp_file")] = static _ => new([ValueType.String], ValueType.String),
                [("fs", "copy_file")] = static _ => new([ValueType.String, ValueType.String], ValueType.Void),
                [("fs", "move_file")] = static _ => new([ValueType.String, ValueType.String], ValueType.Void),
                [("fs", "move_dir")] = static _ => new([ValueType.String, ValueType.String], ValueType.Void),
                [("fs", "move_files_batch")] = static _ => new([ValueType.ArrayOf(ValueType.String), ValueType.ArrayOf(ValueType.String), ValueType.String], ValueType.Void),
                [("fs", "recover_file_moves")] = static _ => new([ValueType.String], ValueType.Void),
                [("fs", "create_dir_all")] = static _ => new([ValueType.String], ValueType.Void),
                [("fs", "create_temp_dir")] = static _ => new([ValueType.String], ValueType.String),
                [("fs", "is_dir")] = static _ => new([ValueType.String], ValueType.Bool),
                [("fs", "remove_dir")] = static _ => new([ValueType.String], ValueType.Void),
                [("fs", "remove_dir_all")] = static _ => new([ValueType.String], ValueType.Void),
                [("path", "script_dir")] = static _ => new([], ValueType.String),
                [("path", "working_dir")] = static _ => new([], ValueType.String),
                [("path", "resolve")] = static _ => new([ValueType.String, ValueType.String], ValueType.String),
                [("path", "relative")] = static _ => new([ValueType.String, ValueType.String], ValueType.String),
                [("path", "is_absolute")] = static _ => new([ValueType.String], ValueType.Bool),
                [("path", "stem")] = static _ => new([ValueType.String], ValueType.String),
                [("path", "change_extension")] = static _ => new([ValueType.String, ValueType.String], ValueType.String),
                [("path", "join")] = static _ => new([ValueType.ArrayOf(ValueType.String)], ValueType.String),
                [("path", "find_duplicate")] = static _ => new([ValueType.ArrayOf(ValueType.String)], ValueType.NullableOf(ValueType.String)),
                [("path", "filename")] = static _ => new([ValueType.String], ValueType.String),
                [("path", "extension")] = static _ => new([ValueType.String], ValueType.String),
                [("path", "parent")] = static _ => new([ValueType.String], ValueType.String),
                [("path", "equal")] = static _ => new([ValueType.String, ValueType.String], ValueType.Bool),
                [("csv", "parse")] = static _ => new([ValueType.String], ValueType.ArrayOf(ValueType.ArrayOf(ValueType.String))),
                [("csv", "rows")] = static _ => new([ValueType.String, ValueType.Encoding, ValueType.ArrayOf(ValueType.String)], ValueType.CsvRows),
                [("csv", "records")] = static _ => new([ValueType.String, ValueType.Encoding], ValueType.CsvRecords),
                [("csv", "parse_table")] = static module => new([ValueType.String], Structure(module, "Table")),
                [("csv", "table")] = static module => new([ValueType.ArrayOf(ValueType.String), ValueType.ArrayOf(ValueType.ArrayOf(ValueType.String))], Structure(module, "Table")),
                [("csv", "row")] = static module => new([ValueType.ArrayOf(ValueType.String), ValueType.ArrayOf(ValueType.String)], Structure(module, "Row")),
                [("csv", "get")] = static module => new([Structure(module, "Row"), ValueType.String], ValueType.String),
                [("csv", "set")] = static module => new([Structure(module, "Row"), ValueType.String, ValueType.String], Structure(module, "Row")),
                [("csv", "stringify_table")] = static module => new([Structure(module, "Table")], ValueType.String),
                [("csv", "has_column")] = static module => new([Structure(module, "Table"), ValueType.String], ValueType.Bool),
                [("csv", "stringify")] = static _ => new([ValueType.ArrayOf(ValueType.ArrayOf(ValueType.String))], ValueType.String),
                [("json", "get")] = static module => new([Structure(module, "Value"), ValueType.String], Structure(module, "Value")),
                [("json", "has")] = static module => new([Structure(module, "Value"), ValueType.String], ValueType.Bool),
                [("json", "remove")] = static module => new([Structure(module, "Value"), ValueType.String], Structure(module, "Value")),
                [("json", "remove_at")] = static module => new([Structure(module, "Value"), ValueType.Int], Structure(module, "Value")),
                [("json", "items")] = static module => new([Structure(module, "Value")], ValueType.ArrayOf(Structure(module, "Value"))),
                [("json", "keys")] = static module => new([Structure(module, "Value")], ValueType.ArrayOf(ValueType.String)),
                [("json", "kind")] = static module => new([Structure(module, "Value")], ValueType.String),
                [("env", "get")] = static _ => new([ValueType.String], ValueType.NullableOf(ValueType.String)),
                [("env", "remove")] = static _ => new([ValueType.String], ValueType.Void),
                [("env", "set")] = static _ => new([ValueType.String, ValueType.String], ValueType.Void),
                [("process", "monotonic_ms")] = static _ => new([], ValueType.Int),
                [("process", "run_to_files")] = static module => new([ValueType.String, ValueType.ArrayOf(ValueType.String), Structure(module, "Options"), ValueType.String, ValueType.String], ValueType.Int),
                [("process", "run")] = static module => new([ValueType.String, ValueType.ArrayOf(ValueType.String), Structure(module, "Options")], Structure(module, "Result")),
                [("process", "options")] = static module => new([ValueType.NullableOf(ValueType.Int), ValueType.Encoding], Structure(module, "Options")),
                [("process", "sleep")] = static _ => new([ValueType.Int], ValueType.Void),
                [("encoding", "parse")] = static _ => new([ValueType.String], ValueType.Encoding),
                [("encoding", "from_code_page")] = static _ => new([ValueType.Int], ValueType.Encoding),
                [("encoding", "name")] = static _ => new([ValueType.Encoding], ValueType.String),
                [("hash", "file")] = static _ => new([ValueType.String, HashValues.Algorithm], ValueType.String),
                [("hash", "text")] = static _ => new([ValueType.String, HashValues.Algorithm, ValueType.Encoding], ValueType.String),
                [("hash", "parse")] = static _ => new([ValueType.String], HashValues.Algorithm),
                [("hash", "name")] = static _ => new([HashValues.Algorithm], ValueType.String),
                [("http", "request")] = static module => new([HttpValues.Method, ValueType.String], Structure(module, "Request")),
                [("http", "options")] = static module => new([ValueType.Int], Structure(module, "Options")),
                [("http", "send")] = static module => new([Structure(module, "Request"), Structure(module, "Options")], Structure(module, "Response")),
                [("http", "download")] = static module => new([Structure(module, "Request"), ValueType.String, ValueType.Int, Structure(module, "Options")], Structure(module, "DownloadResult")),
                [("http", "text")] = static _ => new([HttpValues.Body, ValueType.Encoding], ValueType.String),
                [("http", "header_values")] = static module => new([ValueType.ArrayOf(Structure(module, "Header")), ValueType.String], ValueType.ArrayOf(ValueType.String)),
                [("http", "with_query")] = static module => new([ValueType.String, ValueType.ArrayOf(Structure(module, "QueryParameter"))], ValueType.String),
                [("zip", "create")] = static _ => new([ValueType.String, ValueType.String], ValueType.Void),
                [("zip", "options")] = static module => new([ValueType.Encoding], Structure(module, "Options")),
                [("zip", "list")] = static module => new([ValueType.String, Structure(module, "Options")], ValueType.ArrayOf(Structure(module, "Entry"))),
                [("zip", "extract")] = static module => new([ValueType.String, ValueType.String, Structure(module, "Options")], ValueType.Void),
                [("math", "to_float")] = static _ => new([ValueType.Int], ValueType.Float),
                [("math", "to_int")] = static _ => new([ValueType.Float], ValueType.Int),
                [("math", "trunc")] = static _ => new([ValueType.Float], ValueType.Float),
                [("math", "floor")] = static _ => new([ValueType.Float], ValueType.Float),
                [("math", "ceil")] = static _ => new([ValueType.Float], ValueType.Float),
                [("text", "from_int")] = static _ => new([ValueType.Int], ValueType.String),
                [("text", "length")] = static _ => new([ValueType.String], ValueType.Int),
                [("text", "upper")] = static _ => new([ValueType.String], ValueType.String),
                [("text", "lower")] = static _ => new([ValueType.String], ValueType.String),
                [("text", "slice")] = static _ => new([ValueType.String, ValueType.Int, ValueType.Int], ValueType.String),
                [("text", "index_of")] = static _ => new([ValueType.String, ValueType.String], ValueType.NullableOf(ValueType.Int)),
                [("text", "from_float")] = static _ => new([ValueType.Float], ValueType.String),
                [("text", "from_bool")] = static _ => new([ValueType.Bool], ValueType.String),
                [("text", "parse_int")] = static _ => new([ValueType.String], ValueType.Int),
                [("text", "parse_float")] = static _ => new([ValueType.String], ValueType.Float),
                [("text", "parse_bool")] = static _ => new([ValueType.String], ValueType.Bool),
                [("text", "trim")] = static _ => new([ValueType.String], ValueType.String),
                [("text", "split_lines")] = static _ => new([ValueType.String], ValueType.ArrayOf(ValueType.String)),
                [("text", "contains")] = static _ => new([ValueType.String, ValueType.String], ValueType.Bool),
                [("text", "starts_with")] = static _ => new([ValueType.String, ValueType.String], ValueType.Bool),
                [("text", "ends_with")] = static _ => new([ValueType.String, ValueType.String], ValueType.Bool),
                [("text", "split")] = static _ => new([ValueType.String, ValueType.String], ValueType.ArrayOf(ValueType.String)),
                [("text", "replace_all")] = static _ => new([ValueType.String, ValueType.String, ValueType.String], ValueType.String),
                [("text", "join")] = static _ => new([ValueType.ArrayOf(ValueType.String), ValueType.String], ValueType.String),
                [("test", "assert")] = static _ => new([ValueType.Bool], ValueType.Void),
                [("test", "fail")] = static _ => new([ValueType.String], ValueType.Void),
                [("cli", "command")] = static module => new([ValueType.String, ValueType.String], Structure(module, "Command")),
                [("cli", "argument")] = static module => new([ValueType.String, ValueType.String], Structure(module, "Argument")),
                [("cli", "option")] = static module => new([ValueType.String, ValueType.String], Structure(module, "Option")),
                [("cli", "flag")] = static module => new([ValueType.String, ValueType.String], Structure(module, "Flag")),
                [("cli", "parse")] = static module => new([Structure(module, "Command"), ValueType.ArrayOf(ValueType.String)], Structure(module, "Result")),
                [("cli", "help")] = static module => new([Structure(module, "Command")], ValueType.String),
                [("cli", "help_requested")] = static module => new([Structure(module, "Result")], ValueType.Bool),
                [("cli", "has")] = static module => new([Structure(module, "Result"), ValueType.String], ValueType.Bool),
                [("cli", "get")] = static module => new([Structure(module, "Result"), ValueType.String], ValueType.String),
                [("cli", "get_all")] = static module => new([Structure(module, "Result"), ValueType.String], ValueType.ArrayOf(ValueType.String)),
            };

        private static ValueType Structure(Module module, string name) => module.Structs[name].BoundType;

        public static StandardSignature? Find(Module standardModule, string name)
        {
            if (standardModule.StandardName == "log")
            {
                var operation = LogValues.Operations.FirstOrDefault(value => value.Name == name);
                return operation?.Level is not null ? new([Structure(standardModule, "Logger"), ValueType.String], ValueType.Void)
                    : operation?.Name == "create" ? new([], Structure(standardModule, "Logger"))
                    : operation?.Name == "enabled" ? new([Structure(standardModule, "Logger"), LogValues.Level], ValueType.Bool)
                    : null;
            }
            if (standardModule.StandardName == "datetime")
            {
                var operation = DateTimeValues.Operations.FirstOrDefault(value => value.Name == name);
                return operation is null ? null : new(operation.Parameters, operation.Result);
            }
            return standardModule.StandardName is { } moduleName && Signatures.TryGetValue((moduleName, name), out var signature)
                ? signature(standardModule) : null;
        }
    }
}
