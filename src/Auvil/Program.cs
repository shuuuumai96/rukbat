using Auvil;

return Cli.Run(args, Console.Out, Console.Error);

public static class Cli
{
    private const int Success = 0;
    private const int CompilationFailure = 1;
    private const int UsageOrIoFailure = 2;

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length != 2 || args[0] != "build" || !Path.GetExtension(args[1]).Equals(".auv", StringComparison.OrdinalIgnoreCase))
        {
            stderr.WriteLine("Usage: auvil build input.auv");
            return UsageOrIoFailure;
        }
        try
        {
            var input = Path.GetFullPath(args[1]);
            var result = new Compiler().CompileFile(input);
            foreach (var diagnostic in result.Diagnostics)
                stderr.WriteLine($"{diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): error {diagnostic.Code}: {diagnostic.Message}");
            if (!result.Success) return CompilationFailure;
            var output = Path.ChangeExtension(input, ".ps1");
            File.WriteAllText(output, result.Output, new System.Text.UTF8Encoding(false));
            stdout.WriteLine(output);
            return Success;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            stderr.WriteLine($"error: {error.Message}");
            return UsageOrIoFailure;
        }
    }
}
