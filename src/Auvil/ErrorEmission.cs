namespace Auvil;

internal sealed partial class Emitter
{
    private void EmitGuardedStatements(List<Statement> statements, string location)
    {
        Line($"$__auv_location = {location}");
        Line("try {");
        indentation++;
        EmitStatements(statements);
        indentation--;
        Line("} catch {");
        indentation++;
        Line("& $script:__auv_raise (& $script:__auv_error $_ $__auv_location) $__auv_location");
        indentation--;
        Line("}");
    }

    private void EmitErrorRuntime()
    {
        const string runtime = """
        $__auv_error = {
            param([System.Management.Automation.ErrorRecord]$record, [string]$location)
            $exception = $record.Exception
            while ($null -ne $exception) {
                if ($exception.Data.Contains('Auvil.Location')) {
                    return @{ message = $exception.Message; __auv_location = $exception.Data['Auvil.Location'] }
                }
                $exception = $exception.InnerException
            }
            return @{ message = $record.Exception.Message; __auv_location = $location }
        }
        $__auv_raise = {
            param([hashtable]$value, [string]$location)
            $exception = [System.Exception]::new($value.message)
            if ($value.ContainsKey('__auv_location')) {
                $location = $value.__auv_location
            }
            $exception.Data['Auvil.Location'] = $location
            throw $exception
        }
        """;
        foreach (var line in runtime.Split('\n'))
        {
            Line(line.TrimEnd('\r'));
        }
    }
}
