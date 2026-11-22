namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private void EmitLogRuntime()
        {
            EmitNativeLoader(RuntimeCatalog.Native.Log);
            Line("$__rukbat_log_levels = @{");
            foreach (var level in LogValues.Levels)
            {
                Line($"{Quote(level.Tag)} = @{{ rank = {level.Rank}; label = {Quote(level.Label)} }}");
            }

            Line("}");
            Line("$__rukbat_log_operations = @{");
            foreach (var operation in LogValues.Operations.Where(operation => operation.Level is not null))
            {
                Line($"{Quote(RuntimeCatalog.Modules.Log + RuntimeCatalog.OperationSeparator + operation.Name)} = {Quote(operation.Level!.Tag)}");
            }

            Line("}");
            EmitLines(RuntimeResources.ReadPowerShell(RuntimeCatalog.Resources.LogHelpers));
        }

        private string LogCreate(Call call)
        {
            var fields = checker.Types[call].Struct!.Fields;
            var defaultLevel = Quote(LogValues.DefaultLevel.Tag);
            return $"@{{{Quote(fields["stderr_level"].Key)} = {defaultLevel}; " +
                $"{Quote(fields["file"].Key)} = $null; {Quote(fields["file_level"].Key)} = {defaultLevel}}}";
        }

        private string LogCall(Call call, string operation)
        {
            var argument = call.Arguments[0].Value;
            var loggerFields = checker.Types[argument].NonNullable.Struct!.Fields;
            // Extract the three immutable logger fields before evaluating the message.
            var logger = "(& { $logConfig = " + ReadExpr(argument) + "; return ,([object[]]@(" +
                $"(,$logConfig[{Quote(loggerFields["stderr_level"].Key)}]); " +
                $"(,$logConfig[{Quote(loggerFields["file"].Key)}]); " +
                $"(,$logConfig[{Quote(loggerFields["file_level"].Key)}])" + ")) })";
            return $"(& $script:__rukbat_standard {Quote(operation)} @((,{logger}); (,({Expr(call.Arguments[1].Value)}))))";
        }
    }
}
