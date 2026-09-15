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
        const string runtime = """
        $__rukbat_error = {
            param([System.Management.Automation.ErrorRecord]$record, [string]$location)
            $exception = $record.Exception
            while ($null -ne $exception) {
                if ($exception.Data.Contains('Rukbat.Location')) {
                    return @{ message = $exception.Message; __rukbat_location = $exception.Data['Rukbat.Location'] }
                }
                $exception = $exception.InnerException
            }
            return @{ message = $record.Exception.Message; __rukbat_location = $location }
        }
        $__rukbat_raise = {
            param([hashtable]$value, [string]$location)
            $exception = [System.Exception]::new($value.message)
            if ($value.ContainsKey('__rukbat_location')) {
                $location = $value.__rukbat_location
            }
            $exception.Data['Rukbat.Location'] = $location
            throw $exception
        }
        """;
        foreach (var line in runtime.Split('\n'))
        {
            Line(line.TrimEnd('\r'));
        }
    }
}
