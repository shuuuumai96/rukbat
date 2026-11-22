namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private delegate string CallStrategy(Emitter emitter, Call call, BoundCall binding);

        private static readonly IReadOnlyDictionary<CallKind, CallStrategy> CallStrategies =
            new Dictionary<CallKind, CallStrategy>
            {
                [CallKind.DictionaryMethod] = static (emitter, call, _) => emitter.DictionaryCall(call),
                [CallKind.ArrayAppend] = static (emitter, call, _) => emitter.AppendCall(call),
                [CallKind.Standard] = static (emitter, call, binding) => emitter.StandardCall(call, binding.StandardOperation!),
                [CallKind.StructLiteral] = static (emitter, call, _) => emitter.StructLiteral(call),
                [CallKind.PowerShell] = static (emitter, call, _) => emitter.PowerShellCall(call),
                [CallKind.Arguments] = static (_, _, _) => "$script:__rukbat_arguments",
                [CallKind.Print] = static (emitter, call, _) => emitter.PrintCall(call, "Out"),
                [CallKind.PrintError] = static (emitter, call, _) => emitter.PrintCall(call, "Error"),
                [CallKind.Exit] = static (emitter, call, _) => emitter.ExitCall(call),
                [CallKind.LocalFunction] = static (emitter, call, binding) => emitter.LocalFunctionCall(call, binding.Function!),
                [CallKind.ImportedFunction] = static (emitter, call, binding) => emitter.ImportedFunctionCall(call, binding.Function!)
            };

        private string Call(Call call)
        {
            var binding = checker.Calls[call];
            return CallStrategies[binding.Kind](this, call, binding);
        }

        private string DictionaryCall(Call call)
        {
            var receiver = RawExpr(call.Receiver!);
            var key = call.Arguments.Count == 0 ? "$null" : $"({Expr(call.Arguments[0].Value)})";
            return $"(& $script:__rukbat_dictionary {Quote(call.Name)} ({receiver}) {key} $null {Location(call)} $script:__rukbat_raise)";
        }

        private string AppendCall(Call call)
        {
            var address = Address(call.Receiver!);
            var value = Expr(call.Arguments[0].Value);
            return $"(& $script:__rukbat_append ({address}) ({value}))";
        }

        private string StructLiteral(Call call)
        {
            var fields = call.Arguments.Select(argument =>
                $"{Quote(checker.ConstructionFields[argument].Key)} = {Expr(argument.Value)}");
            return $"@{{{string.Join("; ", fields)}}}";
        }

        private string PrintCall(Call call, string stream) =>
            $"([Console]::{stream}.WriteLine([object]({Expr(call.Arguments[0].Value)})))";

        private string ExitCall(Call call)
        {
            // Process termination crosses module/function boundaries and cannot be caught as Error.
            return $"(& {{ param([long]$code) if ($code -lt 0 -or $code -gt [int]::MaxValue) {{ throw 'exit code must be between 0 and 2147483647.' }}; [Console]::Out.Flush(); [Console]::Error.Flush(); [Environment]::Exit($code) }} ({Expr(call.Arguments[0].Value)}))";
        }

        private IEnumerable<string> FunctionArguments(Call call, FunctionSymbol function)
        {
            var arguments = call.Arguments.Select(argument => Expr(argument.Value));
            // Only the method receiver is borrowed for mutation. Ordinary arguments remain value copies.
            return function.Owner is null ? arguments : arguments.Prepend(RawExpr(call.Receiver!));
        }

        private string LocalFunctionCall(Call call, FunctionSymbol function)
        {
            var arguments = string.Concat(FunctionArguments(call, function).Select(argument => $" ({argument})"));
            return $"({function.PowerShellName}{arguments})";
        }

        private string ImportedFunctionCall(Call call, FunctionSymbol function)
        {
            var arguments = string.Join(", ", FunctionArguments(call, function));
            var receiver = $"$script:__rukbat_dependencies[{Quote(function.Module.Id)}]";
            return $"({receiver}.{function.PowerShellName}({arguments}))";
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
