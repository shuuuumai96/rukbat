namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private static readonly (string Name, string Value)[] CsvTableResultFields =
        [
            ("columns", "(& $script:__rukbat_array $result[0])"),
            ("rows", "$result[1]")
        ];

        // Standard helpers return positional storage. Adapt it to this compilation's
        // bound field keys in one place instead of repeating a branch per result type.
        private static readonly IReadOnlyDictionary<string, (string Name, string Value)[]> StandardResultFields =
            new Dictionary<string, (string Name, string Value)[]>(StringComparer.Ordinal)
            {
                [RuntimeCatalog.Operations.ZipOptions] =
                [
                    ("name_encoding", "$result[0]"),
                    ("max_entries", "$result[1]"),
                    ("max_total_bytes", "$result[2]")
                ],
                [RuntimeCatalog.Operations.ProcessOptions] =
                [
                    ("timeout_ms", "$result[0]"),
                    ("output_encoding", "$result[1]"),
                    ("working_dir", "$null"),
                    ("environment", "([System.Collections.Generic.List[object]]::new())"),
                    ("stdin", "$null")
                ],
                [RuntimeCatalog.Operations.CsvParseTable] = CsvTableResultFields,
                [RuntimeCatalog.Operations.CsvTable] = CsvTableResultFields,
                [RuntimeCatalog.Operations.ProcessRun] =
                [
                    ("exit_code", "[long]$result[0]"),
                    ("stdout", "[string]$result[1]"),
                    ("stderr", "[string]$result[2]")
                ],
                [RuntimeCatalog.Operations.FsStat] =
                [
                    ("is_dir", "$result[0]"),
                    ("size", "$result[1]"),
                    ("modified", "$result[2]")
                ]
            };

        private static readonly IReadOnlyDictionary<string, Func<Emitter, Call, string, string>> StandardModuleCalls =
            new Dictionary<string, Func<Emitter, Call, string, string>>(StringComparer.Ordinal)
            {
                [RuntimeCatalog.Modules.Http] = static (emitter, call, operation) => emitter.HttpCall(call, operation),
                [RuntimeCatalog.Modules.Log] = static (emitter, call, operation) => emitter.LogCall(call, operation),
                [RuntimeCatalog.Modules.Cli] = static (emitter, call, operation) => emitter.CliCall(call, operation)
            };

        private static readonly IReadOnlyDictionary<string, Func<Emitter, Call, string, string>> DirectStandardCalls =
            new Dictionary<string, Func<Emitter, Call, string, string>>(StringComparer.Ordinal)
            {
                [RuntimeCatalog.Operations.LogCreate] = static (emitter, call, _) => emitter.LogCreate(call),
                [RuntimeCatalog.Operations.ReFind] = static (emitter, call, operation) => emitter.RegexFind(call, operation),
                [RuntimeCatalog.Operations.ReFindAll] = static (emitter, call, operation) => emitter.RegexFindAll(call, operation),
                [RuntimeCatalog.Operations.TestAssert] = static (emitter, call, _) => emitter.TestAssertion(call),
                [RuntimeCatalog.Operations.TestFail] = static (emitter, call, _) =>
                    $"(& $script:__rukbat_raise @{{message = ({emitter.Expr(call.Arguments[0].Value)})}} {emitter.Location(call)})",
                [RuntimeCatalog.Operations.JsonParseStrict] = static (emitter, call, _) => emitter.JsonDecode(call, false, strict: true),
                [RuntimeCatalog.Operations.JsonParse] = static (emitter, call, _) => emitter.JsonDecode(call, false),
                [RuntimeCatalog.Operations.JsonAs] = static (emitter, call, _) => emitter.JsonDecode(call, true),
                [RuntimeCatalog.Operations.CsvStringifyTable] = static (emitter, call, operation) => emitter.CsvTableCall(call, operation, stringify: true),
                [RuntimeCatalog.Operations.CsvHasColumn] = static (emitter, call, operation) => emitter.CsvTableCall(call, operation, stringify: false),
                [RuntimeCatalog.Operations.JsonStringify] = static (emitter, call, _) => emitter.JsonEncode(call),
                [RuntimeCatalog.Operations.TextParseEnum] = static (emitter, call, _) => emitter.ParseEnum(call),
                [RuntimeCatalog.Operations.TextFromEnum] = static (emitter, call, _) => emitter.Expr(call.Arguments[0].Value)
            };

        private static bool IsInlinedDirectStandardCall(string operation) =>
            DirectStandardCalls.ContainsKey(operation) && !RuntimeCatalog.DelegatingDirectStandardCalls.Contains(operation);

        private string ParseEnum(Call call)
        {
            var enumeration = checker.Types[call].Enum!;
            return "(& { param([string]$value) " +
                $"if ([Array]::IndexOf({EnumNames(enumeration)}, $value) -lt 0) {{ throw {Quote($"Unknown member of enum {enumeration.Type}.")} }}; " +
                $"return $value }} ({Expr(call.Arguments[0].Value)}))";
        }

        private string JsonEncode(Call call) =>
            $"(& $script:__rukbat_json_encode ({ReadExpr(call.Arguments[0].Value)}) {Schema(checker.Types[call.Arguments[0].Value])})";

        private string JsonDecode(Call call, bool fromValue, bool strict = false) =>
            $"(& $script:__rukbat_json_decode ({Expr(call.Arguments[0].Value)}) {Schema(checker.Types[call])} " +
            (fromValue ? "$true" : "$false") + " $script:__rukbat_dictionary " + (strict ? "$true)" : "$false)");

        private static readonly IReadOnlyDictionary<string, Action<Emitter, Call, string[]>> StandardArgumentAdapters =
            new Dictionary<string, Action<Emitter, Call, string[]>>(StringComparer.Ordinal)
            {
                [RuntimeCatalog.Operations.ZipList] = static (emitter, call, arguments) => emitter.AdaptZipOptions(call, arguments),
                [RuntimeCatalog.Operations.ZipExtract] = static (emitter, call, arguments) => emitter.AdaptZipOptions(call, arguments),
                [RuntimeCatalog.Operations.ProcessRun] = static (emitter, call, arguments) => emitter.AdaptProcessOptions(call, arguments),
                [RuntimeCatalog.Operations.ProcessRunToFiles] = static (emitter, call, arguments) => emitter.AdaptProcessOptions(call, arguments),
                [RuntimeCatalog.Operations.JsonSet] = static (emitter, call, arguments) => emitter.AdaptJsonReplacement(call, arguments),
                [RuntimeCatalog.Operations.JsonSetAt] = static (emitter, call, arguments) => emitter.AdaptJsonReplacement(call, arguments),
                [RuntimeCatalog.Operations.JsonAppend] = static (emitter, call, arguments) => emitter.AdaptJsonReplacement(call, arguments)
            };

        private void AdaptZipOptions(Call call, string[] argumentExpressions)
        {
            var index = call.Arguments.Count - 1;
            var fields = checker.Types[call.Arguments[index].Value].Struct!.Fields;
            argumentExpressions[index] = "(& { $options = " + argumentExpressions[index] + "; return ,([object[]]@(" +
                $"$options[{Quote(fields["name_encoding"].Key)}]; $options[{Quote(fields["max_entries"].Key)}]; " +
                $"$options[{Quote(fields["max_total_bytes"].Key)}]" + ")) })";
        }

        private void AdaptProcessOptions(Call call, string[] argumentExpressions)
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

        private void AdaptJsonReplacement(Call call, string[] argumentExpressions)
        {
            var replacement = call.Arguments[^1].Value;
            var replacementType = checker.Types[replacement];
            argumentExpressions[^1] = replacementType == ValueType.Null ? "[RukbatJsonValue]::new($null)" :
                replacementType.IsJsonValue ? argumentExpressions[^1] :
                $"[RukbatJsonValue]::new([RukbatJsonParser]::Parse((& $script:__rukbat_json_encode ({argumentExpressions[^1]}) {Schema(replacementType)})))";
        }

        private string CsvTableCall(Call call, string standardCall, bool stringify)
        {
            var argumentExpressions = call.Arguments.Select(argument => ReadExpr(argument.Value)).ToArray();
            var fields = checker.Types[call.Arguments[0].Value].Struct!.Fields;
            var secondArgument = stringify
                ? $"$table[{Quote(fields["rows"].Key)}]" : argumentExpressions[1];
            // Evaluate the table expression once before extracting its two fields.
            return $"(& {{ $table = {argumentExpressions[0]}; return (& $script:__rukbat_standard {Quote(standardCall)} @((,$table[{Quote(fields["columns"].Key)}]); (,({secondArgument})))) }})";
        }

        private string StandardCall(Call call, string standardCall)
        {
            var strategy = DirectStandardCalls.GetValueOrDefault(standardCall)
                ?? StandardModuleCalls.GetValueOrDefault(standardCall[..standardCall.IndexOf('.')], EmitDefaultStandardCall);
            return strategy(this, call, standardCall);
        }

        private static string StandardInvocation(string operation, IEnumerable<string> arguments, IReadOnlyList<bool>? wrapElements = null)
        {
            var values = string.Join("; ", arguments.Select((argument, index) =>
                (wrapElements is null || wrapElements[index] ? ",(" : "(") + argument + ")"));
            return $"(& $script:__rukbat_standard {Quote(operation)} @({values}))";
        }

        private static string EmitDefaultStandardCall(Emitter emitter, Call call, string operation) =>
            emitter.StandardRuntimeCall(call, operation);

        private string StandardRuntimeCall(Call call, string standardCall)
        {
            var readOnly = RuntimeCatalog.ReadOnlyStandardCalls.Contains(standardCall);
            var argumentExpressions = call.Arguments.Select((argument, index) =>
                readOnly && (index == call.Arguments.Count - 1 || IsNeverMutated(argument.Value))
                    ? ReadExpr(argument.Value)
                    : Expr(argument.Value)).ToArray();
            var adapted = StandardArgumentAdapters.TryGetValue(standardCall, out var adaptArguments);
            if (adapted)
            {
                adaptArguments!(this, call, argumentExpressions);
            }
            var wrapElements = adapted
                ? null
                : call.Arguments.Select(argument => NeedsArrayElementWrap(checker.Types[argument.Value])).ToArray();
            var invocation = StandardInvocation(standardCall, argumentExpressions, wrapElements);
            if (StandardResultFields.TryGetValue(standardCall, out var resultFields))
            {
                var fields = checker.Types[call].Struct!.Fields;
                var entries = resultFields.Select(field => $"{Quote(fields[field.Name].Key)} = {field.Value}");
                return $"(& {{ $result = {invocation}; return @{{" + string.Join("; ", entries) + "}})";
            }
            var adapt = StandardResultAdapters.GetValueOrDefault(standardCall, AdaptDefaultResult);
            return adapt(invocation, checker.Types[call]);
        }

        private static readonly IReadOnlyDictionary<string, Func<string, ValueType, string>> StandardResultAdapters =
            new Dictionary<string, Func<string, ValueType, string>>(StringComparer.Ordinal)
            {
                ["zip.list"] = AdaptZipList
            };

        private static string AdaptZipList(string invocation, ValueType type)
        {
            var fields = type.Element!.Struct!.Fields;
            return $"(& {{ $items = {invocation}; $entries = [System.Collections.Generic.List[object]]::new(); " +
                $"foreach ($entry in $items) {{ $entries.Add(@{{{Quote(fields["name"].Key)} = $entry[0]; " +
                $"{Quote(fields["is_directory"].Key)} = $entry[1]; {Quote(fields["size"].Key)} = $entry[2]; " +
                $"{Quote(fields["compressed_size"].Key)} = $entry[3]" + "}) }; return ,$entries })";
        }

        private static string AdaptDefaultResult(string invocation, ValueType type) =>
            type.Kind == ValueKind.Array ? $"(& $script:__rukbat_array ({invocation}))" : invocation;
    }
}
