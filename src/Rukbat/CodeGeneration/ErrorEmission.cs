namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitGuardedStatements(List<Statement> statements, string location)
    {
        Line($"$__rukbat_location = {location}");
        Line("try {");
        indentation++;
        EmitStatements(statements);
        indentation--;
        Line("} catch {");
        indentation++;
        Line("& $script:__rukbat_raise (& $script:__rukbat_error $_ $__rukbat_location) $__rukbat_location");
        indentation--;
        Line("}");
    }

    private void EmitErrorRuntime()
    {
        var runtime = RuntimeResources.ReadPowerShell("ErrorRuntime");
        foreach (var line in runtime.Split('\n'))
        {
            Line(line.TrimEnd('\r'));
        }
    }
}
