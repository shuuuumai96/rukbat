using System.ComponentModel;
using System.Diagnostics;
using Auvil;

return Cli.Run(args, Console.Out, Console.Error);

public static class Cli
{
    private const int Success = 0;
    private const int CompilationFailure = 1;
    private const int UsageOrIoFailure = 2;

    private static int Execute(string output)
    {
        // Inherit the caller's directory and standard streams, including interactive input.
        var start = new ProcessStartInfo("pwsh") { UseShellExecute = false };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-File", output })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length != 2 || args[0] is not ("build" or "run") || !Path.GetExtension(args[1]).Equals(".auv", StringComparison.OrdinalIgnoreCase))
        {
            stderr.WriteLine("Usage: auvil <build|run> input.auv");
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
            if (args[0] == "run") return Execute(output);
            stdout.WriteLine(output);
            return Success;
        }
        catch (Win32Exception error)
        {
            stderr.WriteLine($"error: Could not start pwsh. Install PowerShell 7.4 or later and make it available on PATH. {error.Message}");
            return UsageOrIoFailure;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            stderr.WriteLine($"error: {error.Message}");
            return UsageOrIoFailure;
        }
    }
}
