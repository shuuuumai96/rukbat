using System.ComponentModel;
using System.Diagnostics;
using Auvil;

return Cli.Run(args, Console.Out, Console.Error);

public static class Cli
{
    private const int Success = 0;
    private const int CompilationFailure = 1;
    private const int UsageOrIoFailure = 2;
    private const int CompilerArgumentCount = 2;
    private const int ApplicationArgumentsOffset = CompilerArgumentCount + 1;

    private static int Execute(string output, IEnumerable<string> arguments)
    {
        // Inherit the caller's directory and standard streams, including interactive input.
        var start = new ProcessStartInfo("powershell") { UseShellExecute = false };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-File", output })
        {
            start.ArgumentList.Add(argument);
        }
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length < CompilerArgumentCount ||
            args[0] is not ("build" or "run") ||
            !Path.GetExtension(args[1]).Equals(".auv", StringComparison.OrdinalIgnoreCase) ||
            args.Length > CompilerArgumentCount && (args[0] != "run" || args[CompilerArgumentCount] != "--"))
        {
            stderr.WriteLine("Usage: auvil <build|run> input.auv [-- arguments... (run only)]");
            return UsageOrIoFailure;
        }
        try
        {
            var input = Path.GetFullPath(args[1]);
            var result = new Compiler().CompileFile(input);
            foreach (var diagnostic in result.Diagnostics)
            {
                stderr.WriteLine(
                    $"{diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): " +
                    $"error {diagnostic.Code}: {diagnostic.Message}");
            }

            if (!result.Success)
            {
                return CompilationFailure;
            }

            var output = Path.ChangeExtension(input, ".ps1");
            // Windows PowerShell 5.1 identifies UTF-8 source reliably only when it has a BOM.
            File.WriteAllText(output, result.Output, new System.Text.UTF8Encoding(true));
            if (args[0] == "run")
            {
                return Execute(output, args.Skip(ApplicationArgumentsOffset));
            }

            stdout.WriteLine(output);
            return Success;
        }
        catch (Win32Exception error)
        {
            stderr.WriteLine(
                "error: Could not start Windows PowerShell 5.1-compatible 'powershell' " +
                $"and make it available on PATH. {error.Message}");
            return UsageOrIoFailure;
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            stderr.WriteLine($"error: {error.Message}");
            return UsageOrIoFailure;
        }
    }
}
