namespace Rukbat;

internal sealed partial class Emitter
{
    private string StandardCall(Call call, string standardCall)
    {
        if (standardCall.StartsWith("http.", StringComparison.Ordinal)) return HttpCall(call, standardCall);
        if (standardCall == "test.assert")
        {
            return TestAssertion(call);
        }
        if (standardCall == "test.fail")
        {
            return $"(& $script:__rukbat_raise @{{message = ({Expr(call.Arguments[0].Value)})}} {Location(call)})";
        }
        if (standardCall == "json.parse")
        {
            return $"(& $script:__rukbat_json_decode ({Expr(call.Arguments[0].Value)}) {Schema(checker.Types[call])} $false $script:__rukbat_dictionary)";
        }
        if (standardCall == "json.as")
        {
            return $"(& $script:__rukbat_json_decode ({Expr(call.Arguments[0].Value)}) {Schema(checker.Types[call])} $true $script:__rukbat_dictionary)";
        }
        if (standardCall == "json.stringify")
        {
            return $"(& $script:__rukbat_json_encode ({Expr(call.Arguments[0].Value)}) {Schema(checker.Types[call.Arguments[0].Value])})";
        }
        var argumentExpressions = call.Arguments.Select(argument => Expr(argument.Value)).ToArray();
        if (standardCall.StartsWith("cli.", StringComparison.Ordinal))
            return CliCall(call, standardCall);
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
        if (standardCall == "zip.options")
        {
            var fields = checker.Types[call].Struct!.Fields;
            return $"(& {{ $result = {invocation}; return @{{" +
                $"{Quote(fields["name_encoding"].Key)} = $result[0]; {Quote(fields["max_entries"].Key)} = $result[1]; " +
                $"{Quote(fields["max_total_bytes"].Key)} = $result[2]" + "}})";
        }
        if (standardCall == "zip.list")
        {
            var fields = checker.Types[call].Element!.Struct!.Fields;
            return $"(& {{ $items = {invocation}; $entries = [System.Collections.Generic.List[object]]::new(); " +
                $"foreach ($entry in $items) {{ $entries.Add(@{{{Quote(fields["name"].Key)} = $entry[0]; " +
                $"{Quote(fields["is_directory"].Key)} = $entry[1]; {Quote(fields["size"].Key)} = $entry[2]; " +
                $"{Quote(fields["compressed_size"].Key)} = $entry[3]" + "}) }; return ,$entries })";
        }
        if (standardCall == "process.options")
        {
            var fields = checker.Types[call].Struct!.Fields;
            return $"(& {{ $result = {invocation}; return @{{" +
                $"{Quote(fields["timeout_ms"].Key)} = $result[0]; {Quote(fields["output_encoding"].Key)} = $result[1]; " +
                $"{Quote(fields["working_dir"].Key)} = $null; {Quote(fields["environment"].Key)} = (& $script:__rukbat_array @()); " +
                $"{Quote(fields["stdin"].Key)} = $null" + "}})";
        }
        if (standardCall is "csv.parse_table" or "csv.table")
        {
            var fields = checker.Types[call].Struct!.Fields;
            return $"(& {{ $result = {invocation}; return @{{" +
                $"{Quote(fields["columns"].Key)} = (& $script:__rukbat_array $result[0]); " +
                $"{Quote(fields["rows"].Key)} = (& $script:__rukbat_array $result[1])" + "}})";
        }
        if (standardCall == "process.run")
        {
            var fields = checker.Types[call].Struct!.Fields;
            return $"(& {{ $result = {invocation}; return @{{" +
                $"{Quote(fields["exit_code"].Key)} = [int]$result[0]; " +
                $"{Quote(fields["stdout"].Key)} = [string]$result[1]; " +
                $"{Quote(fields["stderr"].Key)} = [string]$result[2]" + "}})";
        }
        if (standardCall == "fs.stat")
        {
            var fields = checker.Types[call].Struct!.Fields;
            return $"(& {{ $result = {invocation}; return @{{" +
                $"{Quote(fields["is_dir"].Key)} = $result[0]; " +
                $"{Quote(fields["size"].Key)} = $result[1]; " +
                $"{Quote(fields["modified"].Key)} = $result[2]" + "}})";
        }
        if (checker.Types[call].Element is not null)
        {
            return $"(& $script:__rukbat_array ({invocation}))";
        }
        return invocation;
    }
}
