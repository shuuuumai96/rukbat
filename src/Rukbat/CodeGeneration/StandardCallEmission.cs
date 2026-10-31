namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private static readonly (string Name, string Value)[] CsvTableResultFields =
        [
            ("columns", "(& $script:__rukbat_array $result[0])"),
            ("rows", "(& $script:__rukbat_array $result[1])")
        ];

        // Standard helpers return positional storage. Adapt it to this compilation's
        // bound field keys in one place instead of repeating a branch per result type.
        private static readonly IReadOnlyDictionary<string, (string Name, string Value)[]> StandardResultFields =
            new Dictionary<string, (string Name, string Value)[]>(StringComparer.Ordinal)
            {
                ["zip.options"] =
                [
                    ("name_encoding", "$result[0]"),
                    ("max_entries", "$result[1]"),
                    ("max_total_bytes", "$result[2]")
                ],
                ["process.options"] =
                [
                    ("timeout_ms", "$result[0]"),
                    ("output_encoding", "$result[1]"),
                    ("working_dir", "$null"),
                    ("environment", "(& $script:__rukbat_array @())"),
                    ("stdin", "$null")
                ],
                ["csv.parse_table"] = CsvTableResultFields,
                ["csv.table"] = CsvTableResultFields,
                ["process.run"] =
                [
                    ("exit_code", "[long]$result[0]"),
                    ("stdout", "[string]$result[1]"),
                    ("stderr", "[string]$result[2]")
                ],
                ["fs.stat"] =
                [
                    ("is_dir", "$result[0]"),
                    ("size", "$result[1]"),
                    ("modified", "$result[2]")
                ]
            };

        private static readonly IReadOnlyDictionary<string, Func<Emitter, Call, string, string>> StandardModuleCalls =
            new Dictionary<string, Func<Emitter, Call, string, string>>(StringComparer.Ordinal)
            {
                ["http"] = static (emitter, call, operation) => emitter.HttpCall(call, operation),
                ["log"] = static (emitter, call, operation) => emitter.LogCall(call, operation),
                ["cli"] = static (emitter, call, operation) => emitter.CliCall(call, operation)
            };

        private static readonly IReadOnlyDictionary<string, Func<Emitter, Call, string>> DirectStandardCalls =
            new Dictionary<string, Func<Emitter, Call, string>>(StringComparer.Ordinal)
            {
                ["re.find"] = static (emitter, call) => emitter.RegexCall(call, "re.find"),
                ["re.find_all"] = static (emitter, call) => emitter.RegexCall(call, "re.find_all"),
                ["test.assert"] = static (emitter, call) => emitter.TestAssertion(call),
                ["test.fail"] = static (emitter, call) =>
                    $"(& $script:__rukbat_raise @{{message = ({emitter.Expr(call.Arguments[0].Value)})}} {emitter.Location(call)})",
                ["json.parse"] = static (emitter, call) => emitter.JsonDecode(call, false),
                ["json.as"] = static (emitter, call) => emitter.JsonDecode(call, true),
                ["json.stringify"] = static (emitter, call) => emitter.JsonEncode(call)
            };

        private string JsonEncode(Call call) =>
            $"(& $script:__rukbat_json_encode ({Expr(call.Arguments[0].Value)}) {Schema(checker.Types[call.Arguments[0].Value])})";

        private string JsonDecode(Call call, bool fromValue) =>
            $"(& $script:__rukbat_json_decode ({Expr(call.Arguments[0].Value)}) {Schema(checker.Types[call])} " +
            (fromValue ? "$true" : "$false") + " $script:__rukbat_dictionary)";

        private string StandardCall(Call call, string standardCall)
        {
            var moduleName = standardCall[..standardCall.IndexOf('.')];
            if (StandardModuleCalls.TryGetValue(moduleName, out var emitModuleCall))
            {
                return emitModuleCall(this, call, standardCall);
            }
            if (DirectStandardCalls.TryGetValue(standardCall, out var emitCall))
            {
                return emitCall(this, call);
            }

            var argumentExpressions = call.Arguments.Select(argument => Expr(argument.Value)).ToArray();
            if (standardCall is "zip.list" or "zip.extract")
            {
                var index = call.Arguments.Count - 1;
                var fields = checker.Types[call.Arguments[index].Value].Struct!.Fields;
                argumentExpressions[index] = "(& { $options = " + argumentExpressions[index] + "; return ,([object[]]@(" +
                    $"$options[{Quote(fields["name_encoding"].Key)}]; $options[{Quote(fields["max_entries"].Key)}]; " +
                    $"$options[{Quote(fields["max_total_bytes"].Key)}]" + ")) })";
            }
            if (standardCall == "process.run")
            {
                var options = checker.Types[call.Arguments[2].Value].Struct!.Fields;
                var environment = options["environment"].Type.Element!.Struct!.Fields;
                var input = options["stdin"].Type.NonNullable.Struct!.Fields;
                argumentExpressions[2] = "(& { $options = " + argumentExpressions[2] + "; " +
                    "$changes = [System.Collections.Generic.List[object]]::new(); " +
                    $"foreach ($entry in $options[{Quote(options["environment"].Key)}]) {{ " +
                    $"$changes.Add([object[]]@($entry[{Quote(environment["name"].Key)}], $entry[{Quote(environment["value"].Key)}])) }}; " +
                    $"$inputValue = $options[{Quote(options["stdin"].Key)}]; $inputData = $null; " +
                    $"if ($null -ne $inputValue) {{ $inputData = [object[]]@($inputValue[{Quote(input["text"].Key)}], $inputValue[{Quote(input["encoding"].Key)}]) }}; " +
                    "return ,([object[]]@(" +
                    $"$options[{Quote(options["timeout_ms"].Key)}]; $options[{Quote(options["output_encoding"].Key)}]; " +
                    $"$options[{Quote(options["working_dir"].Key)}]; (,$changes); (,$inputData)))" + " })";
            }
            if (standardCall is "json.set" or "json.set_at" or "json.append")
            {
                var replacement = call.Arguments[^1].Value;
                var replacementType = checker.Types[replacement];
                argumentExpressions[^1] = replacementType == ValueType.Null ? "[RukbatJsonValue]::new($null)" :
                    replacementType.IsJsonValue ? argumentExpressions[^1] :
                    $"[RukbatJsonValue]::new([RukbatJsonParser]::Parse((& $script:__rukbat_json_encode ({argumentExpressions[^1]}) {Schema(replacementType)})))";
            }
            if (standardCall is "csv.stringify_table" or "csv.has_column")
            {
                var fields = checker.Types[call.Arguments[0].Value].Struct!.Fields;
                var secondArgument = standardCall == "csv.stringify_table"
                    ? $"$table[{Quote(fields["rows"].Key)}]" : argumentExpressions[1];
                // Evaluate the table expression once before extracting its two fields.
                return $"(& {{ $table = {argumentExpressions[0]}; return (& $script:__rukbat_standard {Quote(standardCall)} @((,$table[{Quote(fields["columns"].Key)}]); (,({secondArgument})))) }})";
            }
            var standardArguments = string.Join("; ", argumentExpressions.Select(expression => ",(" + expression + ")"));
            var invocation = $"(& $script:__rukbat_standard {Quote(standardCall)} @({standardArguments}))";
            if (StandardResultFields.TryGetValue(standardCall, out var resultFields))
            {
                var fields = checker.Types[call].Struct!.Fields;
                var entries = resultFields.Select(field => $"{Quote(fields[field.Name].Key)} = {field.Value}");
                return $"(& {{ $result = {invocation}; return @{{" + string.Join("; ", entries) + "}})";
            }
            if (standardCall == "zip.list")
            {
                var fields = checker.Types[call].Element!.Struct!.Fields;
                return $"(& {{ $items = {invocation}; $entries = [System.Collections.Generic.List[object]]::new(); " +
                    $"foreach ($entry in $items) {{ $entries.Add(@{{{Quote(fields["name"].Key)} = $entry[0]; " +
                    $"{Quote(fields["is_directory"].Key)} = $entry[1]; {Quote(fields["size"].Key)} = $entry[2]; " +
                    $"{Quote(fields["compressed_size"].Key)} = $entry[3]" + "}) }; return ,$entries })";
            }
            if (checker.Types[call].Element is not null)
            {
                return $"(& $script:__rukbat_array ({invocation}))";
            }
            return invocation;
        }
    }
}
