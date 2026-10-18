namespace Rukbat;

internal sealed record StandardSignature(ValueType[] Parameters, ValueType Result);

// Context-dependent generic operations (array and JSON) are checked separately.
// Bound struct types must come from this compilation's canonical standard module.
internal static class StandardSignatures
{
    public static StandardSignature? Find(Module standardModule, string name)
    {
        ValueType Structure(string type) => standardModule.Structs[type].BoundType;
        if (standardModule.StandardName == "log")
        {
            var operation = LogValues.Operations.FirstOrDefault(value => value.Name == name);
            return operation?.Level is not null ? new([Structure("Logger"), ValueType.String], ValueType.Void)
                : operation?.Name == "create" ? new([], Structure("Logger"))
                : operation?.Name == "enabled" ? new([Structure("Logger"), LogValues.Level], ValueType.Bool)
                : null;
        }
        if (standardModule.StandardName == "datetime")
        {
            var operation = DateTimeValues.Operations.FirstOrDefault(value => value.Name == name);
            return operation is null ? null : new(operation.Parameters, operation.Result);
        }
        return (standardModule.StandardName, name) switch
        {
            ("re", "compile") => new([ValueType.String, ValueType.Int], ValueType.RegexPattern),
            ("re", "is_match") => new([ValueType.String, ValueType.RegexPattern], ValueType.Bool),
            ("re", "find") => new([ValueType.String, ValueType.RegexPattern], ValueType.NullableOf(Structure("Match"))),
            ("re", "find_all") => new([ValueType.String, ValueType.RegexPattern], ValueType.ArrayOf(Structure("Match"))),
            ("re", "replace_all" or "replace_all_template") => new([ValueType.String, ValueType.RegexPattern, ValueType.String], ValueType.String),
            ("re", "split") => new([ValueType.String, ValueType.RegexPattern], ValueType.ArrayOf(ValueType.String)),
            ("re", "escape") => new([ValueType.String], ValueType.String),
            ("fs", "stat") => new([ValueType.String], Structure("Info")),
            ("fs", "list_files") => new([ValueType.String, ValueType.String], ValueType.ArrayOf(ValueType.String)),
            ("fs", "list_files_recursive") => new([ValueType.String, ValueType.String], ValueType.ArrayOf(ValueType.String)),
            ("fs", "list_dirs") => new([ValueType.String, ValueType.String], ValueType.ArrayOf(ValueType.String)),
            ("fs", "read_text") => new([ValueType.String, ValueType.Encoding], ValueType.String),
            ("fs", "lines") => new([ValueType.String, ValueType.Encoding], ValueType.FileLines),
            ("fs", "is_file") => new([ValueType.String], ValueType.Bool),
            ("fs", "remove_file") => new([ValueType.String], ValueType.Void),
            ("fs", "write_text") => new([ValueType.String, ValueType.String, ValueType.Encoding], ValueType.Void),
            ("fs", "append_text") => new([ValueType.String, ValueType.String, ValueType.Encoding], ValueType.Void),
            ("fs", "create_temp_file") => new([ValueType.String], ValueType.String),
            ("fs", "copy_file") => new([ValueType.String, ValueType.String], ValueType.Void),
            ("fs", "move_file") => new([ValueType.String, ValueType.String], ValueType.Void),
            ("fs", "move_dir") => new([ValueType.String, ValueType.String], ValueType.Void),
            ("fs", "move_files_batch") => new([ValueType.ArrayOf(ValueType.String), ValueType.ArrayOf(ValueType.String), ValueType.String], ValueType.Void),
            ("fs", "recover_file_moves") => new([ValueType.String], ValueType.Void),
            ("fs", "create_dir_all") => new([ValueType.String], ValueType.Void),
            ("fs", "create_temp_dir") => new([ValueType.String], ValueType.String),
            ("fs", "is_dir") => new([ValueType.String], ValueType.Bool),
            ("fs", "remove_dir") => new([ValueType.String], ValueType.Void),
            ("fs", "remove_dir_all") => new([ValueType.String], ValueType.Void),
            ("path", "script_dir") => new([], ValueType.String),
            ("path", "working_dir") => new([], ValueType.String),
            ("path", "resolve") => new([ValueType.String, ValueType.String], ValueType.String),
            ("path", "relative") => new([ValueType.String, ValueType.String], ValueType.String),
            ("path", "is_absolute") => new([ValueType.String], ValueType.Bool),
            ("path", "stem") => new([ValueType.String], ValueType.String),
            ("path", "change_extension") => new([ValueType.String, ValueType.String], ValueType.String),
            ("path", "join") => new([ValueType.ArrayOf(ValueType.String)], ValueType.String),
            ("path", "find_duplicate") => new([ValueType.ArrayOf(ValueType.String)], ValueType.NullableOf(ValueType.String)),
            ("path", "filename") => new([ValueType.String], ValueType.String),
            ("path", "extension") => new([ValueType.String], ValueType.String),
            ("path", "parent") => new([ValueType.String], ValueType.String),
            ("path", "equal") => new([ValueType.String, ValueType.String], ValueType.Bool),
            ("csv", "parse") => new([ValueType.String], ValueType.ArrayOf(ValueType.ArrayOf(ValueType.String))),
            ("csv", "parse_table") => new([ValueType.String], Structure("Table")),
            ("csv", "table") => new([ValueType.ArrayOf(ValueType.String), ValueType.ArrayOf(ValueType.ArrayOf(ValueType.String))], Structure("Table")),
            ("csv", "row") => new([ValueType.ArrayOf(ValueType.String), ValueType.ArrayOf(ValueType.String)], Structure("Row")),
            ("csv", "get") => new([Structure("Row"), ValueType.String], ValueType.String),
            ("csv", "set") => new([Structure("Row"), ValueType.String, ValueType.String], Structure("Row")),
            ("csv", "stringify_table") => new([Structure("Table")], ValueType.String),
            ("csv", "has_column") => new([Structure("Table"), ValueType.String], ValueType.Bool),
            ("csv", "stringify") => new([ValueType.ArrayOf(ValueType.ArrayOf(ValueType.String))], ValueType.String),
            ("json", "get") => new([Structure("Value"), ValueType.String], Structure("Value")),
            ("json", "has") => new([Structure("Value"), ValueType.String], ValueType.Bool),
            ("json", "remove") => new([Structure("Value"), ValueType.String], Structure("Value")),
            ("json", "remove_at") => new([Structure("Value"), ValueType.Int], Structure("Value")),
            ("json", "items") => new([Structure("Value")], ValueType.ArrayOf(Structure("Value"))),
            ("json", "keys") => new([Structure("Value")], ValueType.ArrayOf(ValueType.String)),
            ("json", "kind") => new([Structure("Value")], ValueType.String),
            ("env", "get") => new([ValueType.String], ValueType.NullableOf(ValueType.String)),
            ("env", "remove") => new([ValueType.String], ValueType.Void),
            ("env", "set") => new([ValueType.String, ValueType.String], ValueType.Void),
            ("process", "run") => new([ValueType.String, ValueType.ArrayOf(ValueType.String), Structure("Options")], Structure("Result")),
            ("process", "options") => new([ValueType.NullableOf(ValueType.Int), ValueType.Encoding], Structure("Options")),
            ("encoding", "parse") => new([ValueType.String], ValueType.Encoding),
            ("encoding", "from_code_page") => new([ValueType.Int], ValueType.Encoding),
            ("encoding", "name") => new([ValueType.Encoding], ValueType.String),
            ("hash", "file") => new([ValueType.String, HashValues.Algorithm], ValueType.String),
            ("hash", "text") => new([ValueType.String, HashValues.Algorithm, ValueType.Encoding], ValueType.String),
            ("hash", "parse") => new([ValueType.String], HashValues.Algorithm),
            ("hash", "name") => new([HashValues.Algorithm], ValueType.String),
            ("http", "request") => new([HttpValues.Method, ValueType.String], Structure("Request")),
            ("http", "options") => new([ValueType.Int], Structure("Options")),
            ("http", "send") => new([Structure("Request"), Structure("Options")], Structure("Response")),
            ("http", "download") => new([Structure("Request"), ValueType.String, ValueType.Int, Structure("Options")], Structure("DownloadResult")),
            ("http", "text") => new([HttpValues.Body, ValueType.Encoding], ValueType.String),
            ("http", "header_values") => new([ValueType.ArrayOf(Structure("Header")), ValueType.String], ValueType.ArrayOf(ValueType.String)),
            ("http", "with_query") => new([ValueType.String, ValueType.ArrayOf(Structure("QueryParameter"))], ValueType.String),
            ("zip", "create") => new([ValueType.String, ValueType.String], ValueType.Void),
            ("zip", "options") => new([ValueType.Encoding], Structure("Options")),
            ("zip", "list") => new([ValueType.String, Structure("Options")], ValueType.ArrayOf(Structure("Entry"))),
            ("zip", "extract") => new([ValueType.String, ValueType.String, Structure("Options")], ValueType.Void),
            ("math", "to_float") => new([ValueType.Int], ValueType.Float),
            ("math", "to_int") => new([ValueType.Float], ValueType.Int),
            ("math", "trunc") => new([ValueType.Float], ValueType.Float),
            ("math", "floor") => new([ValueType.Float], ValueType.Float),
            ("math", "ceil") => new([ValueType.Float], ValueType.Float),
            ("text", "from_int") => new([ValueType.Int], ValueType.String),
            ("text", "length") => new([ValueType.String], ValueType.Int),
            ("text", "upper") => new([ValueType.String], ValueType.String),
            ("text", "lower") => new([ValueType.String], ValueType.String),
            ("text", "slice") => new([ValueType.String, ValueType.Int, ValueType.Int], ValueType.String),
            ("text", "index_of") => new([ValueType.String, ValueType.String], ValueType.NullableOf(ValueType.Int)),
            ("text", "from_float") => new([ValueType.Float], ValueType.String),
            ("text", "from_bool") => new([ValueType.Bool], ValueType.String),
            ("text", "parse_int") => new([ValueType.String], ValueType.Int),
            ("text", "parse_float") => new([ValueType.String], ValueType.Float),
            ("text", "parse_bool") => new([ValueType.String], ValueType.Bool),
            ("text", "trim") => new([ValueType.String], ValueType.String),
            ("text", "split_lines") => new([ValueType.String], ValueType.ArrayOf(ValueType.String)),
            ("text", "contains") => new([ValueType.String, ValueType.String], ValueType.Bool),
            ("text", "starts_with") => new([ValueType.String, ValueType.String], ValueType.Bool),
            ("text", "ends_with") => new([ValueType.String, ValueType.String], ValueType.Bool),
            ("text", "split") => new([ValueType.String, ValueType.String], ValueType.ArrayOf(ValueType.String)),
            ("text", "replace_all") => new([ValueType.String, ValueType.String, ValueType.String], ValueType.String),
            ("text", "join") => new([ValueType.ArrayOf(ValueType.String), ValueType.String], ValueType.String),
            ("test", "assert") => new([ValueType.Bool], ValueType.Void),
            ("test", "fail") => new([ValueType.String], ValueType.Void),
            ("cli", "command") => new([ValueType.String, ValueType.String], Structure("Command")),
            ("cli", "argument") => new([ValueType.String, ValueType.String], Structure("Argument")),
            ("cli", "option") => new([ValueType.String, ValueType.String], Structure("Option")),
            ("cli", "flag") => new([ValueType.String, ValueType.String], Structure("Flag")),
            ("cli", "parse") => new([Structure("Command"), ValueType.ArrayOf(ValueType.String)], Structure("Result")),
            ("cli", "help") => new([Structure("Command")], ValueType.String),
            ("cli", "help_requested") => new([Structure("Result")], ValueType.Bool),
            ("cli", "has") => new([Structure("Result"), ValueType.String], ValueType.Bool),
            ("cli", "get") => new([Structure("Result"), ValueType.String], ValueType.String),
            ("cli", "get_all") => new([Structure("Result"), ValueType.String], ValueType.ArrayOf(ValueType.String)),
            _ => null
        };
    }
}
