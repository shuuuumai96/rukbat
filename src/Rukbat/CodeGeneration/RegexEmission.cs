namespace Rukbat;

internal sealed partial class Emitter
{
    private string RegexCall(Call call, string operation)
    {
        var arguments = call.Arguments.Select(argument => ",(" + Expr(argument.Value) + ")");
        var invocation = $"(& $script:__rukbat_standard {Quote(operation)} @({string.Join("; ", arguments)}))";
        if (operation == "re.find")
        {
            var match = RegexMatch("$regexResult", checker.Types[call].NonNullable.Struct!);
            return $"(& {{ $regexResult = {invocation}; if ($null -eq $regexResult) {{ return $null }}; return {match} }})";
        }

        var item = RegexMatch("$regexItem", checker.Types[call].Element!.Struct!);
        return $"(& {{ $regexItems = {invocation}; $regexResults = [System.Collections.Generic.List[object]]::new(); " +
            $"foreach ($regexItem in $regexItems) {{ $regexResults.Add({item}) }}; return ,$regexResults }})";
    }

    // Native match rows carry ordinary data; public fields use this compilation's bound keys.
    private static string RegexMatch(string expression, StructSymbol structure)
    {
        var fields = structure.Fields;
        return "@{" + $"{Quote(fields["value"].Key)} = {expression}[0]; " +
            $"{Quote(fields["start"].Key)} = {expression}[1]; " +
            $"{Quote(fields["count"].Key)} = {expression}[2]; " +
            $"{Quote(fields["groups"].Key)} = {expression}[3]" + "}";
    }
}
