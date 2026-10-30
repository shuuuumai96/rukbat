namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private string Call(Call call)
        {
            if (checker.DictionaryCalls.Contains(call))
            {
                return $"(& $script:__rukbat_dictionary {Quote(call.Name)} ({RawExpr(call.Receiver!)}) " +
                    (call.Arguments.Count == 0 ? "$null" : $"({Expr(call.Arguments[0].Value)})") + $" $null {Location(call)} $script:__rukbat_raise)";
            }

            if (checker.ArrayAppends.Contains(call))
            {
                var address = Address(call.Receiver!);
                if (call.Arguments.Count != 1)
                {
                    return "$null";
                }
                var value = Expr(call.Arguments[0].Value);
                return $"({address} = (& $script:__rukbat_append ({address}) ({value})))";
            }
            if (checker.StandardCalls.TryGetValue(call, out var standardCall))
            {
                return StandardCall(call, standardCall);
            }
            if (checker.Constructions.ContainsKey(call))
            {
                var fields = call.Arguments.Select(argument =>
                    $"{Quote(checker.ConstructionFields[argument].Key)} = {Expr(argument.Value)}");
                return $"@{{{string.Join("; ", fields)}}}";
            }

            if (call.PowerShellType is not null)
            {
                return PowerShellCall(call);
            }
            if (call.Receiver is null && call.Name == "args")
            {
                return "$script:__rukbat_arguments";
            }
            if (call.Receiver is null && call.Name is "print" or "eprint")
            {
                var stream = call.Name == "eprint" ? "Error" : "Out";
                return $"([Console]::{stream}.WriteLine([object]({Expr(call.Arguments[0].Value)})))";
            }
            if (call.Receiver is null && call.Name == "exit")
            {
                // Process termination crosses module/function boundaries and cannot be caught as Error.
                return $"(& {{ param([long]$code) if ($code -lt 0 -or $code -gt [int]::MaxValue) {{ throw 'exit code must be between 0 and 2147483647.' }}; [Console]::Out.Flush(); [Console]::Error.Flush(); [Environment]::Exit($code) }} ({Expr(call.Arguments[0].Value)}))";
            }

            var function = checker.Calls[call];
            var arguments = call.Arguments.Select(a => Expr(a.Value));
            // Only the method receiver is borrowed for mutation. Ordinary arguments remain value copies.
            if (function.Owner is not null)
            {
                arguments = arguments.Prepend(RawExpr(call.Receiver!));
            }

            var values = arguments.ToArray();
            if (function.Module == module)
            {
                return $"({function.PowerShellName}{string.Concat(values.Select(a => " (" + a + ")"))})";
            }

            var receiver = $"$script:__rukbat_dependencies[{Quote(function.Module.Id)}]";
            return $"({receiver}.{function.PowerShellName}({string.Join(", ", values)}))";
        }

        private string PowerShellCall(Call call)
        {
            // The first argument is the command. Split the rest into positional and named splats.
            var positional = string.Join("; ", call.Arguments
                .Skip(1)
                .Where(argument => argument.Name is null)
                .Select(argument => ",(" + PowerShellArgument(argument.Value) + ")"));
            var named = string.Join("; ", call.Arguments
                .Skip(1)
                .Where(argument => argument.Name is not null)
                .Select(argument => $"{Quote(argument.Name!)} = {PowerShellArgument(argument.Value)}"));

            return $"(& $script:__rukbat_runtime {Schema(checker.Types[call])} " +
                $"({Expr(call.Arguments[0].Value)}) @({positional}) @{{{named}}} {Location(call)} $script:__rukbat_error $script:__rukbat_raise)";
        }

        private string PowerShellArgument(Expression expression)
        {
            var value = Expr(expression);
            return checker.Types[expression].NonNullable.Element is null
                ? value
                : $"(& {{ param($value) if ($null -eq $value) {{ return $null }}; return ,([object[]]$value) }} ({value}))";
        }
    }
}
